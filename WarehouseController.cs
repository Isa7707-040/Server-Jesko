using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;

namespace StoreSystem.Api.Controllers;

// ═════════════════════════════════════════════════════════════
//  OMBOR (WAREHOUSE) — faqat O'QIYDIGAN (read-only) hisobot kontrolleri.
//  Admin panelidagi "Ombor" bo'limi shu endpointlardan foydalanadi.
//  Bu kontroller HECH NARSANI o'zgartirmaydi (yozmaydi) — shuning uchun mavjud
//  savdo/kirim jarayonlarini buza olmaydi. Faqat mavjud ma'lumotni jamlaydi.
// ═════════════════════════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
public class WarehouseController : ControllerBase
{
    private readonly AppDbContext _db;
    public WarehouseController(AppDbContext db) => _db = db;

    // ───────────────────────────────────────────────────────
    //  OMBOR UMUMIY RO'YXATI
    //  Barcha mahsulotlar + qoldiq soni + oxirgi kirim sanasi + oxirgi firma
    //  + jami kirim qilingan dona. "Tugagan" (qoldiq <= 0) alohida belgilanadi.
    // ───────────────────────────────────────────────────────
    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] int lowThreshold = 5)
    {
        var products = await _db.Products.OrderBy(p => p.Name).ToListAsync();

        // Har bir mahsulot bo'yicha kirim yig'indisi (oxirgi sana + jami dona).
        var incomeAgg = await _db.Stocks
            .GroupBy(s => s.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                LastDate = g.Max(x => x.CreatedAt),
                TotalAdded = g.Sum(x => x.QuantityAdded),
                IncomeCount = g.Count()
            })
            .ToListAsync();
        var incomeMap = incomeAgg.ToDictionary(x => x.ProductId);

        // Har bir mahsulotning oxirgi firmasi (kirim qatorlaridan).
        var piList = await _db.PurchaseItems
            .Where(i => i.Purchase != null)
            .Select(i => new
            {
                i.ProductId,
                Date = i.Purchase!.CreatedAt,
                SupplierName = i.Purchase.Supplier != null ? i.Purchase.Supplier.Name : null
            })
            .ToListAsync();
        var lastSupplierMap = piList
            .GroupBy(x => x.ProductId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.Date).First().SupplierName);

        var result = products.Select(p =>
        {
            incomeMap.TryGetValue(p.Id, out var inc);
            lastSupplierMap.TryGetValue(p.Id, out var supplier);
            int remaining = p.TotalPieces;
            string status = remaining <= 0 ? "out" : (remaining <= lowThreshold ? "low" : "ok");
            return new
            {
                p.Id,
                p.Name,
                p.Unit,
                p.QuantityInBlock,
                Remaining = remaining,            // qoldiq (dona)
                p.BuyPriceBlock,
                p.SellPriceBlock,
                p.SellPricePiece,
                p.CreatedAt,                      // mahsulot yaratilgan sana
                LastIncomeDate = inc?.LastDate,   // oxirgi kirim sanasi (null = kirim yo'q)
                TotalReceived = inc?.TotalAdded ?? 0,
                IncomeCount = inc?.IncomeCount ?? 0,
                LastSupplier = supplier,
                IsOutOfStock = remaining <= 0,
                IsLow = remaining > 0 && remaining <= lowThreshold,
                Status = status                   // "out" | "low" | "ok"
            };
        }).ToList();

        return Ok(new
        {
            Products = result,
            TotalProducts = result.Count,
            OutOfStock = result.Count(r => r.IsOutOfStock),
            LowStock = result.Count(r => r.IsLow),
            InStock = result.Count(r => !r.IsOutOfStock)
        });
    }

    // ───────────────────────────────────────────────────────
    //  BITTA MAHSULOT KIRIM TARIXI
    //  "Qaysi firma, nechta dona, qancha summa, qaysi sana" — to'liq tarix.
    //  Ikki manbadan yig'iladi (takrorlanmasdan):
    //    1) Kirim (Purchase) qatorlari — firma, blok, dona, narx, summa aniq.
    //    2) Qo'lda qo'shilgan stok (AddStock) — firmasiz qo'lda to'ldirish.
    // ───────────────────────────────────────────────────────
    [HttpGet("product/{id}/income-history")]
    public async Task<IActionResult> IncomeHistory(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product == null) return NotFound();

        // 1) Kirim (Purchase) qatorlari — firma va summa aniq.
        var purchaseEntries = await _db.PurchaseItems
            .Where(i => i.ProductId == id && i.Purchase != null)
            .Include(i => i.Purchase).ThenInclude(p => p!.Supplier)
            .OrderByDescending(i => i.Purchase!.CreatedAt)
            .ToListAsync();

        var entries = new List<object>();

        foreach (var it in purchaseEntries)
        {
            int piecesPerBlock = it.PiecesPerBlock <= 0 ? 1 : it.PiecesPerBlock;
            int pieces = it.Quantity * piecesPerBlock;
            double lineTotal = it.Quantity * it.UnitCost;
            entries.Add(new
            {
                Date = it.Purchase!.CreatedAt,
                Supplier = it.Purchase.Supplier?.Name,
                Blocks = it.Quantity,           // necha blok/birlik olindi
                PiecesPerBlock = piecesPerBlock,
                Pieces = pieces,                // omborga qo'shilgan dona
                UnitCost = it.UnitCost,         // 1 blok/birlik xarid narxi
                Sum = lineTotal,                // qator summasi (aniq)
                Source = "Kirim",
                PurchaseId = it.PurchaseId,
                PurchaseStatus = it.Purchase.Status,
                DueDate = it.Purchase.DueDate,
                Note = it.Purchase.Note
            });
        }

        // 2) Qo'lda qo'shilgan stok (Purchase orqali emas). Purchase Stock yozuvlari
        //    Note = "Firma: ..." bilan belgilangani uchun ularni chetlab o'tamiz
        //    (takror hisoblanmasin).
        var manualStocks = await _db.Stocks
            .Where(s => s.ProductId == id && (s.Note == null || !s.Note.StartsWith("Firma:")))
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        foreach (var s in manualStocks)
        {
            entries.Add(new
            {
                Date = s.CreatedAt,
                Supplier = (string?)null,       // qo'lda — firma yo'q
                Blocks = (int?)null,
                PiecesPerBlock = (int?)null,
                Pieces = s.QuantityAdded,
                UnitCost = s.BuyPrice,
                Sum = (double?)null,            // qo'lda kirimda aniq summa yo'q
                Source = "Qo'lda",
                PurchaseId = (int?)null,
                PurchaseStatus = (string?)null,
                DueDate = (DateTime?)null,
                Note = s.Note
            });
        }

        var ordered = entries
            .OrderByDescending(e => (DateTime)e.GetType().GetProperty("Date")!.GetValue(e)!)
            .ToList();

        return Ok(new
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Remaining = product.TotalPieces,
            Entries = ordered
        });
    }

    // ───────────────────────────────────────────────────────
    //  OYLIK STATISTIKA
    //  Berilgan oy uchun (standart: joriy oy):
    //   • eng ko'p kirim qilingan mahsulotlar (dona bo'yicha),
    //   • eng kam kirim qilingan mahsulotlar,
    //   • narxi qimmatlashgan mahsulotlar (bu oydagi oxirgi narx > avvalgi narx),
    //   • narxi arzonlashgan mahsulotlar.
    //  Sana chegaralari UTC bo'yicha [oy boshi, keyingi oy boshi).
    // ───────────────────────────────────────────────────────
    [HttpGet("monthly-stats")]
    public async Task<IActionResult> MonthlyStats([FromQuery] int? year, [FromQuery] int? month, [FromQuery] int top = 5)
    {
        var now = DateTime.UtcNow;
        int y = year ?? now.Year;
        int m = month ?? now.Month;
        if (m < 1) m = 1; if (m > 12) m = 12;
        if (top < 1) top = 1; if (top > 50) top = 50;

        var start = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);

        // Mahsulot nomlari lug'ati.
        var productNames = await _db.Products.ToDictionaryAsync(p => p.Id, p => p.Name);

        // ── Kirim hajmi (dona) shu oyda ──
        var monthIncomes = await _db.Stocks
            .Where(s => s.CreatedAt >= start && s.CreatedAt < end)
            .GroupBy(s => s.ProductId)
            .Select(g => new { ProductId = g.Key, Pieces = g.Sum(x => x.QuantityAdded), Count = g.Count() })
            .ToListAsync();

        var incomeRanked = monthIncomes
            .Select(x => new
            {
                x.ProductId,
                Name = productNames.TryGetValue(x.ProductId, out var n) ? n : $"#{x.ProductId}",
                Pieces = x.Pieces,
                IncomeCount = x.Count
            })
            .ToList();

        var topReceived = incomeRanked.OrderByDescending(x => x.Pieces).Take(top).ToList();
        var leastReceived = incomeRanked.Where(x => x.Pieces > 0).OrderBy(x => x.Pieces).Take(top).ToList();

        // ── Narx o'zgarishlari (blok xarid narxi) ──
        var thisMonthPrices = await _db.PurchaseItems
            .Where(i => i.Purchase != null && i.Purchase.CreatedAt >= start && i.Purchase.CreatedAt < end && i.UnitCost > 0)
            .Select(i => new { i.ProductId, i.UnitCost, Date = i.Purchase!.CreatedAt })
            .ToListAsync();
        var beforePrices = await _db.PurchaseItems
            .Where(i => i.Purchase != null && i.Purchase.CreatedAt < start && i.UnitCost > 0)
            .Select(i => new { i.ProductId, i.UnitCost, Date = i.Purchase!.CreatedAt })
            .ToListAsync();

        var latestThisMonth = thisMonthPrices
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Date).First().UnitCost);
        var latestBefore = beforePrices
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Date).First().UnitCost);

        var priceChanges = new List<dynamic>();
        foreach (var kv in latestThisMonth)
        {
            if (!latestBefore.TryGetValue(kv.Key, out double oldPrice)) continue; // avvalgi narx yo'q — taqqoslab bo'lmaydi
            double newPrice = kv.Value;
            if (Math.Abs(newPrice - oldPrice) < 0.0001) continue; // o'zgarmagan
            double diff = newPrice - oldPrice;
            double percent = oldPrice > 0 ? diff / oldPrice * 100.0 : 0;
            priceChanges.Add(new
            {
                ProductId = kv.Key,
                Name = productNames.TryGetValue(kv.Key, out var n) ? n : $"#{kv.Key}",
                OldPrice = oldPrice,
                NewPrice = newPrice,
                Diff = diff,
                Percent = Math.Round(percent, 2)
            });
        }

        var priceIncreases = priceChanges.Where(c => c.Diff > 0).OrderByDescending(c => c.Percent).ToList();
        var priceDecreases = priceChanges.Where(c => c.Diff < 0).OrderBy(c => c.Percent).ToList();

        return Ok(new
        {
            Year = y,
            Month = m,
            PeriodStart = start,
            PeriodEnd = end,
            TopReceived = topReceived,
            LeastReceived = leastReceived,
            PriceIncreases = priceIncreases,
            PriceDecreases = priceDecreases,
            TotalIncomePieces = incomeRanked.Sum(x => x.Pieces),
            ProductsReceived = incomeRanked.Count
        });
    }
}
