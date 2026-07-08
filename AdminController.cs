using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminController(AppDbContext db) => _db = db;

    // ─────────────────────────────────
    // BARCHA MA'LUMOTLARNI TOZALASH (Factory reset)
    // Xodimlar, mijozlar, FIRMALAR, mahsulotlar, KIRIMLAR (Purchases)
    // va kirim qatorlari, ombor qoldiqlari (Stocks), buyurtmalar,
    // QAYTARISHLAR (Returns), REVIZIYALAR (Revisions), firma to'lovlari,
    // qarz to'lovlari, shtrix-kodlar va sinxron tarixi — hammasini
    // BUTUNLAY o'chiradi. So'ng baza bo'sh qolmasligi (admin qulflanib
    // qolmasligi) uchun yangi standart admin yaratiladi:
    // username = admin, parol = 7707
    //
    // ESLATMA: Settings (umumiy sozlamalar — masalan buxgalter
    // o'chirish paroli) biznes ma'lumoti emas, shuning uchun ataylab
    // saqlanadi va bu tozalashda o'chirilmaydi.
    // ─────────────────────────────────
    [HttpPost("reset")]
    public async Task ResetAll([FromBody] ResetRequest req)
    {
        // Oddiy himoya: noto'g'ri kalit bilan tasodifan chaqirilmasligi uchun
        if (req?.ConfirmKey != "7707")
            return Unauthorized(new { message = "Tasdiqlash kaliti noto'g'ri." });

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // FK bog'lanishlarini hisobga olib, bolalardan (child) boshlab
            // ota (parent) jadvallarga qarab o'chiramiz. Restrict bog'lanishlar
            // buzilmasligi uchun tartib MUHIM:
            //   Return -> Order: avval ReturnItems + Returns, keyin Orders
            //   Purchase -> Supplier (Restrict): avval Purchases, keyin Suppliers
            //   PurchaseItem -> Product (Restrict): avval PurchaseItems, keyin Products
            //   Order -> User (Restrict): avval Orders, keyin Users

            // ── Qaytarishlar (vozvrat) — Orders'dan OLDIN ──
            await TryExecuteAsync("DELETE FROM ReturnItems");
            await TryExecuteAsync("DELETE FROM Returns");

            // ── Reviziyalar (inventarizatsiya) ──
            await TryExecuteAsync("DELETE FROM RevisionItems");
            await TryExecuteAsync("DELETE FROM Revisions");

            await _db.Database.ExecuteSqlRawAsync("DELETE FROM OrderItems");
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Orders");
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Stocks");
            await TryExecuteAsync("DELETE FROM ProductBarcodes");
            await TryExecuteAsync("DELETE FROM DebtPayments");
            await TryExecuteAsync("DELETE FROM PurchaseItems");
            await TryExecuteAsync("DELETE FROM SupplierPayments");
            await TryExecuteAsync("DELETE FROM Purchases");
            await TryExecuteAsync("DELETE FROM Suppliers");
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Products");
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Clients");
            await TryExecuteAsync("DELETE FROM SyncOperations");
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Users");

            // Identity (ID hisoblagich) larni 0 ga qaytaramiz — yangi yozuvlar 1 dan boshlanadi
            await ReseedAsync("ReturnItems");
            await ReseedAsync("Returns");
            await ReseedAsync("RevisionItems");
            await ReseedAsync("Revisions");
            await ReseedAsync("OrderItems");
            await ReseedAsync("Orders");
            await ReseedAsync("Stocks");
            await ReseedAsync("ProductBarcodes");
            await ReseedAsync("DebtPayments");
            await ReseedAsync("PurchaseItems");
            await ReseedAsync("SupplierPayments");
            await ReseedAsync("Purchases");
            await ReseedAsync("Suppliers");
            await ReseedAsync("Products");
            await ReseedAsync("Clients");
            await ReseedAsync("SyncOperations");
            await ReseedAsync("Users");

            // Standart admin (qulflanib qolmaslik uchun)
            _db.Users.Add(new User
            {
                FullName = "Administrator",
                Username = "admin",
                Password = "7707",
                Role = "Admin",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            await tx.CommitAsync();
            return Ok(new { message = "Barcha ma'lumotlar tozalandi. Standart admin: admin / 7707" });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return StatusCode(500, new { message = "Tozalashda xatolik: " + ex.Message });
        }
    }

    // Reseed qilinishi mumkin bo'lgan jadvallarning QAT'IY ro'yxati.
    // Faqat shu nomlar qabul qilinadi — tashqaridan kelgan qiymat ishlatilmaydi,
    // shu sabab SQL injection imkoni yo'q.
    private static readonly string[] _reseedableTables =
        { "OrderItems", "Orders", "Stocks", "ProductBarcodes", "DebtPayments",
          "PurchaseItems", "SupplierPayments", "Purchases", "Suppliers",
          "Products", "Clients", "SyncOperations", "Users",
          "ReturnItems", "Returns", "RevisionItems", "Revisions" };

    private async Task ReseedAsync(string table)
    {
        // Ro'yxatda yo'q nom — umuman bajarilmaydi
        if (Array.IndexOf(_reseedableTables, table) < 0) return;
        try
        {
            // SQLite: AUTOINCREMENT hisoblagichi 'sqlite_sequence' jadvalida saqlanadi.
            // O'sha satrni o'chirsak — keyingi ID lar 1 dan boshlanadi.
            // 'table' yuqoridagi qat'iy ro'yxatdan keladi (foydalanuvchi kiritmaydi) — injection yo'q.
            var sql = "DELETE FROM sqlite_sequence WHERE name = '" + table + "'";
#pragma warning disable EF1002 // Jadval nomi ichki whitelist'dan — injection xavfi yo'q
            await _db.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
        }
        catch { /* sqlite_sequence yo'q bo'lsa — muhim emas */ }
    }

    // Jadval hali yaratilmagan bo'lishi mumkin (masalan ProductBarcodes) —
    // bunday holda DELETE xato bermay, e'tiborsiz qoldiriladi.
    private async Task TryExecuteAsync(string sql)
    {
        try { await _db.Database.ExecuteSqlRawAsync(sql); }
        catch { /* jadval yo'q — muhim emas */ }
    }
}

public class ResetRequest
{
    public string? ConfirmKey { get; set; }
}
