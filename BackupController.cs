using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using System.Text;

namespace StoreSystem.Api.Controllers;

// ===================================================================
//  ZAXIRA (BACKUP) / TIKLASH (RESTORE)
//  Butun ma'lumotlar bazasini bitta faylga saqlash va o'sha fayldan
//  to'liq tiklash. SQLite (bitta fayl) bo'lgani uchun zaxira = bazaning
//  to'liq, izchil nusxasi.
// ===================================================================
[ApiController]
[Route("api/[controller]")]
public class BackupController : ControllerBase
{
    private readonly AppDbContext _db;
    public BackupController(AppDbContext db) => _db = db;

    // Tiklash/zaxira ishlaydigan jadvallarning QAT'IY ro'yxati.
    // INSERT tartibi: avval "ota" jadvallar, keyin "bola" jadvallar (FK uchun xavfsiz).
    private static readonly string[] _tablesParentFirst =
        { "Users", "Clients", "Suppliers", "Products", "ProductBarcodes", "DebtPayments",
          "SupplierPayments", "Stocks", "Orders", "OrderItems", "Purchases", "PurchaseItems",
          "SyncOperations", "Returns", "ReturnItems" };

    // SQLite fayl boshidagi "sehrli" imzo: "SQLite format 3\0"
    private static readonly byte[] _sqliteMagic = Encoding.ASCII.GetBytes("SQLite format 3\0");

    // -- 1) JORIY BAZA HAQIDA QISQA MA'LUMOT --
    [HttpGet("info")]
    public async Task<IActionResult> Info()
    {
        try
        {
            var info = new
            {
                products = await _db.Products.CountAsync(),
                clients = await _db.Clients.CountAsync(),
                users = await _db.Users.CountAsync(),
                orders = await _db.Orders.CountAsync(),
                serverTime = DateTime.Now
            };
            return Ok(info);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Ma'lumot olishda xatolik: " + ex.Message });
        }
    }

    // -- 2) ZAXIRA YUKLAB OLISH (export) --
    [HttpGet("download")]
    public async Task<IActionResult> Download()
    {
        ServerConfig.EnsureDir();
        var tempPath = Path.Combine(ServerConfig.DataDir, $"export-{Guid.NewGuid():N}.db");

        try
        {
            var escaped = tempPath.Replace("'", "''");
            await _db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{escaped}'");

            var bytes = await System.IO.File.ReadAllBytesAsync(tempPath);

            var fileName = $"JESKO-baza-{DateTime.Now:yyyy-MM-dd-HHmm}.jeskobackup";
            return File(bytes, "application/octet-stream", fileName);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Zaxira yaratishda xatolik: " + ex.Message });
        }
        finally
        {
            try { if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath); } catch { }
        }
    }

    // -- 3) ZAXIRADAN TIKLASH (restore) --
    [HttpPost("restore")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Restore(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Fayl tanlanmadi yoki bo'sh." });

        ServerConfig.EnsureDir();
        var incomingPath = Path.Combine(ServerConfig.DataDir, $"restore-{Guid.NewGuid():N}.db");

        try
        {
            await using (var fs = System.IO.File.Create(incomingPath))
                await file.CopyToAsync(fs);

            var (valid, reason, userCount) = ValidateBackup(incomingPath);
            if (!valid)
                return BadRequest(new { message = "Bu fayl yaroqli JESKO zaxira fayli emas. " + reason });

            TrySafetyCopy();

            await RestoreFromAttachedAsync(incomingPath);

            var summary = new
            {
                products = await _db.Products.CountAsync(),
                clients = await _db.Clients.CountAsync(),
                users = await _db.Users.CountAsync(),
                orders = await _db.Orders.CountAsync()
            };

            return Ok(new
            {
                message = "Baza muvaffaqiyatli tiklandi.",
                restoredUsers = userCount,
                summary
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Tiklashda xatolik: " + ex.Message });
        }
        finally
        {
            try { if (System.IO.File.Exists(incomingPath)) System.IO.File.Delete(incomingPath); } catch { }
        }
    }

    // -- Zaxira faylini tekshirish --
    private static (bool ok, string reason, int userCount) ValidateBackup(string path)
    {
        try
        {
            using (var fs = System.IO.File.OpenRead(path))
            {
                var head = new byte[16];
                int read = fs.Read(head, 0, 16);
                if (read < 16 || !head.AsSpan(0, 16).SequenceEqual(_sqliteMagic))
                    return (false, "Fayl tuzilishi noto'g'ri.", 0);
            }

            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();

            using var conn = new SqliteConnection(cs);
            conn.Open();

            if (!TableExists(conn, "Users"))
                return (false, "Ichida 'Users' (xodimlar) jadvali topilmadi.", 0);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            var count = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            if (count < 1)
                return (false, "Zaxirada birorta ham xodim yo'q (bo'sh baza).", 0);

            return (true, "", count);
        }
        catch (Exception ex)
        {
            return (false, "Faylni o'qib bo'lmadi: " + ex.Message, 0);
        }
    }

    // -- Joriy bazaning xavfsizlik nusxasini olish (tiklashdan oldin) --
    private static void TrySafetyCopy()
    {
        try
        {
            if (!System.IO.File.Exists(ServerConfig.DbPath)) return;
            var backupsDir = Path.Combine(ServerConfig.DataDir, "backups");
            Directory.CreateDirectory(backupsDir);
            var dest = Path.Combine(backupsDir, $"avto-zaxira-{DateTime.Now:yyyy-MM-dd-HHmmss}.db");
            System.IO.File.Copy(ServerConfig.DbPath, dest, overwrite: true);
        }
        catch { }
    }

    // -- Asosiy tiklash mantig'i --
    private async Task RestoreFromAttachedAsync(string incomingPath)
    {
        var conn = (SqliteConnection)_db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        var escaped = incomingPath.Replace("'", "''");

        await ExecAsync(conn, "PRAGMA foreign_keys = OFF;");
        await ExecAsync(conn, $"ATTACH DATABASE '{escaped}' AS src;");

        try
        {
            await using var tx = await conn.BeginTransactionAsync();

            foreach (var table in _tablesParentFirst.Reverse())
                await ExecInTxAsync(conn, tx, $"DELETE FROM main.\"{table}\";");

            foreach (var table in _tablesParentFirst)
            {
                if (!TableExists(conn, table, "src")) continue;
                var mainCols = GetColumns(conn, "main", table);
                var srcCols = GetColumns(conn, "src", table);
                var shared = mainCols.Where(c => srcCols.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
                if (shared.Count == 0) continue;
                string colList = string.Join(", ", shared.Select(c => $"\"{c}\""));
                await ExecInTxAsync(conn, tx,
                    $"INSERT INTO main.\"{table}\" ({colList}) SELECT {colList} FROM src.\"{table}\";");
            }

            if (TableExists(conn, "sqlite_sequence", "main") && TableExists(conn, "sqlite_sequence", "src"))
            {
                await ExecInTxAsync(conn, tx, "DELETE FROM main.sqlite_sequence;");
                await ExecInTxAsync(conn, tx, "INSERT INTO main.sqlite_sequence SELECT * FROM src.sqlite_sequence;");
            }

            await tx.CommitAsync();
        }
        finally
        {
            await ExecAsync(conn, "DETACH DATABASE src;");
            await ExecAsync(conn, "PRAGMA foreign_keys = ON;");
        }
    }

    // -- Yordamchi metodlar --
    private static List<string> GetColumns(SqliteConnection conn, string schema, string table)
    {
        var cols = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA {schema}.table_info(\"{table}\");";
        using var r = cmd.ExecuteReader();
        while (r.Read()) cols.Add(r.GetString(1));
        return cols;
    }

    private static bool TableExists(SqliteConnection conn, string table, string schema = "main")
    {
        using var cmd = conn.CreateCommand();
        if (string.Equals(table, "sqlite_sequence", StringComparison.OrdinalIgnoreCase))
            cmd.CommandText = $"SELECT COUNT(*) FROM {schema}.sqlite_master WHERE name = 'sqlite_sequence';";
        else
            cmd.CommandText = $"SELECT COUNT(*) FROM {schema}.sqlite_master WHERE type = 'table' AND name = $n;";
        cmd.Parameters.AddWithValue("$n", table);
        try { return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0; }
        catch { return false; }
    }

    private static async Task ExecAsync(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ExecInTxAsync(SqliteConnection conn, System.Data.Common.DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
