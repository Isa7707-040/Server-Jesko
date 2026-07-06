using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoreSystem.Api.Data;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace StoreSystem.Api
{
    // Web serverni (Kestrel + API) talab bo'yicha ishga tushiradi/to'xtatadi.
    public class ServerHost
    {
        private WebApplication? _app;
        private DiscoveryService? _discovery;

        public bool IsRunning => _app != null;
        public int RunningPort { get; private set; }

        public static bool IsPortFree(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static int FindFreePort(int preferred)
        {
            for (int p = preferred; p <= preferred + 20 && p <= 65535; p++)
                if (IsPortFree(p)) return p;
            return -1;
        }

        public void Start(int port)
        {
            if (_app != null) return;

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = AppContext.BaseDirectory
            });

            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

            builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = null);
            builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
            {
                o.MultipartBodyLengthLimit = long.MaxValue;
                o.ValueLengthLimit = int.MaxValue;
            });

            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.ReferenceHandler =
                        System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
                });
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite($"Data Source={ServerConfig.DbPath}"));
            builder.Services.AddCors(options =>
                options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

            var app = builder.Build();

            // SQLite: jadvallarni model bo'yicha yaratadi (+ admin seed).
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
                EnsureSchemaUpgrades(db); // eski bazalarga yangi ustun/jadvallarni qo'shadi
            }

            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseCors("AllowAll");
            app.MapControllers();

            Task.Run(() => app.StartAsync()).GetAwaiter().GetResult();

            _app = app;
            RunningPort = port;

            try
            {
                _discovery = new DiscoveryService(port);
                _discovery.Start();
            }
            catch { }
        }

        public void Stop()
        {
            if (_app == null) return;

            var app = _app;
            _app = null;
            RunningPort = 0;

            try { _discovery?.Stop(); } catch { }
            _discovery = null;

            try
            {
                Task.Run(async () =>
                {
                    await app.StopAsync(TimeSpan.FromSeconds(5));
                    await app.DisposeAsync();
                }).GetAwaiter().GetResult();
            }
            catch { }
        }

        // SXEMA YANGILANISHI: EnsureCreated mavjud jadvalni o'zgartirmaydi.
        // Yangi versiyada qo'shilgan ustun/jadvallar eski bazalarda ham bo'lishi
        // uchun - yo'q bo'lsa, ularni xavfsiz qo'shamiz (ma'lumotlar saqlanadi).
        private static void EnsureSchemaUpgrades(AppDbContext db)
        {
            AddColumnIfMissing(db, "Orders", "CashAmount", "REAL NOT NULL DEFAULT 0");
            AddColumnIfMissing(db, "Orders", "CardAmount", "REAL NOT NULL DEFAULT 0");

            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""Suppliers"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Name"" TEXT NOT NULL DEFAULT '',
                ""Phone"" TEXT NULL,
                ""Note"" TEXT NULL,
                ""DebtBalance"" REAL NOT NULL DEFAULT 0,
                ""DebtDueDate"" TEXT NULL,
                ""DebtReminderDone"" INTEGER NOT NULL DEFAULT 0,
                ""CreatedAt"" TEXT NOT NULL DEFAULT '');");

            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""Purchases"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""SupplierId"" INTEGER NOT NULL DEFAULT 0,
                ""TotalSum"" REAL NOT NULL DEFAULT 0,
                ""PaidSum"" REAL NOT NULL DEFAULT 0,
                ""Status"" TEXT NOT NULL DEFAULT 'Paid',
                ""DueDate"" TEXT NULL,
                ""Note"" TEXT NULL,
                ""CreatedAt"" TEXT NOT NULL DEFAULT '');");

            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""PurchaseItems"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""PurchaseId"" INTEGER NOT NULL DEFAULT 0,
                ""ProductId"" INTEGER NOT NULL DEFAULT 0,
                ""Quantity"" INTEGER NOT NULL DEFAULT 0,
                ""PiecesPerBlock"" INTEGER NOT NULL DEFAULT 1,
                ""UnitCost"" REAL NOT NULL DEFAULT 0);");

            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""SupplierPayments"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""SupplierId"" INTEGER NOT NULL DEFAULT 0,
                ""Amount"" REAL NOT NULL DEFAULT 0,
                ""RemainingAfter"" REAL NOT NULL DEFAULT 0,
                ""PaidAt"" TEXT NOT NULL DEFAULT '',
                ""Note"" TEXT NULL);");

            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""SyncOperations"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""OperationId"" TEXT NOT NULL DEFAULT '',
                ""Type"" TEXT NOT NULL DEFAULT '',
                ""EntityId"" INTEGER NOT NULL DEFAULT 0,
                ""Amount"" REAL NOT NULL DEFAULT 0,
                ""AppliedAmount"" REAL NOT NULL DEFAULT 0,
                ""Note"" TEXT NULL,
                ""ClientCreatedAt"" TEXT NOT NULL DEFAULT '',
                ""AppliedAt"" TEXT NOT NULL DEFAULT '',
                ""Device"" TEXT NULL);");

            ExecRaw(db, @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SyncOperations_OperationId""
                ON ""SyncOperations"" (""OperationId"");");

            AddColumnIfMissing(db, "Purchases", "ReminderDone", "INTEGER NOT NULL DEFAULT 0");

            AddColumnIfMissing(db, "Suppliers", "DebtDueDate", "TEXT NULL");
            AddColumnIfMissing(db, "Suppliers", "DebtReminderDone", "INTEGER NOT NULL DEFAULT 0");

            AddColumnIfMissing(db, "Clients", "DebtDueDate", "TEXT NULL");
            AddColumnIfMissing(db, "Clients", "DebtReminderDone", "INTEGER NOT NULL DEFAULT 0");

            AddColumnIfMissing(db, "DebtPayments", "CashierId", "INTEGER NULL");
            AddColumnIfMissing(db, "DebtPayments", "PaymentType", "TEXT NOT NULL DEFAULT 'Cash'");
            AddColumnIfMissing(db, "DebtPayments", "Source", "TEXT NULL");

            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""Settings"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Key"" TEXT NOT NULL DEFAULT '',
                ""Value"" TEXT NOT NULL DEFAULT '');");
            ExecRaw(db, @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Settings_Key""
                ON ""Settings"" (""Key"");");

            // -- QAYTARISH (VOZVRAT) ustun va jadvallari (yangi) --
            AddColumnIfMissing(db, "Orders", "RefundedSum", "REAL NOT NULL DEFAULT 0");
            AddColumnIfMissing(db, "OrderItems", "ReturnedQuantity", "INTEGER NOT NULL DEFAULT 0");

            // -- SKIDKA (CHEGIRMA): chegirmadan oldingi asl narx --
            AddColumnIfMissing(db, "OrderItems", "OriginalPrice", "REAL NOT NULL DEFAULT 0");

            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""Returns"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""OrderId"" INTEGER NOT NULL DEFAULT 0,
                ""CashierId"" INTEGER NULL,
                ""TotalSum"" REAL NOT NULL DEFAULT 0,
                ""CashAmount"" REAL NOT NULL DEFAULT 0,
                ""CardAmount"" REAL NOT NULL DEFAULT 0,
                ""DebtReduced"" REAL NOT NULL DEFAULT 0,
                ""PaymentType"" TEXT NOT NULL DEFAULT 'Cash',
                ""CreatedAt"" TEXT NOT NULL DEFAULT '');");

            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""ReturnItems"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""ReturnId"" INTEGER NOT NULL DEFAULT 0,
                ""ProductId"" INTEGER NOT NULL DEFAULT 0,
                ""OrderItemId"" INTEGER NOT NULL DEFAULT 0,
                ""Quantity"" INTEGER NOT NULL DEFAULT 0,
                ""Price"" REAL NOT NULL DEFAULT 0);");

            AddColumnIfMissing(db, "Returns", "OrderId", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(db, "Returns", "CashierId", "INTEGER NULL");
            AddColumnIfMissing(db, "Returns", "TotalSum", "REAL NOT NULL DEFAULT 0");
            AddColumnIfMissing(db, "Returns", "CashAmount", "REAL NOT NULL DEFAULT 0");
            AddColumnIfMissing(db, "Returns", "CardAmount", "REAL NOT NULL DEFAULT 0");
            AddColumnIfMissing(db, "Returns", "DebtReduced", "REAL NOT NULL DEFAULT 0");
            AddColumnIfMissing(db, "Returns", "PaymentType", "TEXT NOT NULL DEFAULT 'Cash'");
            AddColumnIfMissing(db, "Returns", "CreatedAt", "TEXT NOT NULL DEFAULT ''");

            AddColumnIfMissing(db, "ReturnItems", "ReturnId", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(db, "ReturnItems", "ProductId", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(db, "ReturnItems", "OrderItemId", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(db, "ReturnItems", "Quantity", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(db, "ReturnItems", "Price", "REAL NOT NULL DEFAULT 0");

            // -- REVIZIYA (INVENTARIZATSIYA) jadvallari (yangi) --
            // EnsureCreated mavjud bazaga yangi jadval qo'shmaydi - qo'lda yaratamiz.
            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""Revisions"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""CreatedAt"" TEXT NOT NULL DEFAULT '',
                ""CompletedAt"" TEXT NULL,
                ""CreatedByUserId"" INTEGER NULL,
                ""CreatedByName"" TEXT NOT NULL DEFAULT '',
                ""Status"" TEXT NOT NULL DEFAULT 'Draft',
                ""Note"" TEXT NULL);");

            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""RevisionItems"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""RevisionId"" INTEGER NOT NULL DEFAULT 0,
                ""ProductId"" INTEGER NOT NULL DEFAULT 0,
                ""ProductName"" TEXT NOT NULL DEFAULT '',
                ""Unit"" TEXT NOT NULL DEFAULT 'dona',
                ""QuantityInBlock"" INTEGER NOT NULL DEFAULT 1,
                ""SystemQty"" INTEGER NOT NULL DEFAULT 0,
                ""CountedQty"" INTEGER NOT NULL DEFAULT 0,
                ""IsCounted"" INTEGER NOT NULL DEFAULT 0,
                ""BuyPriceBlock"" REAL NOT NULL DEFAULT 0);");
        }

        private static void ExecRaw(AppDbContext db, string sql)
        {
            var conn = db.Database.GetDbConnection();
            bool wasClosed = conn.State != System.Data.ConnectionState.Open;
            if (wasClosed) conn.Open();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
            finally
            {
                if (wasClosed) conn.Close();
            }
        }

        private static void AddColumnIfMissing(AppDbContext db, string table, string column, string definition)
        {
            var conn = db.Database.GetDbConnection();
            bool wasClosed = conn.State != System.Data.ConnectionState.Open;
            if (wasClosed) conn.Open();
            try
            {
                bool exists = false;
                using (var check = conn.CreateCommand())
                {
                    check.CommandText = $"PRAGMA table_info(\"{table}\");";
                    using var r = check.ExecuteReader();
                    while (r.Read())
                    {
                        if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                        { exists = true; break; }
                    }
                }
                if (!exists)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};";
                    alter.ExecuteNonQuery();
                }
            }
            finally
            {
                if (wasClosed) conn.Close();
            }
        }
    }
}
