using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Controllers;

// ===================================================================
//  REVIZIYA (INVENTARIZATSIYA) — ombor sonlarini haqiqatga moslash.
//  Mavjud savdo/kirim jarayonlariga TEGILMAYDI. Faqat reviziya "complete"
//  qilinganda Product.TotalPieces sanab chiqilgan songa tenglanadi.
// ===================================================================
[ApiController]
[Route("api/[controller]")]
public class RevisionsController : ControllerBase
{
    private readonly AppDbContext _db;
    public RevisionsController(AppDbContext db) => _db = db;

    private static double PiecePrice(double buyBlock, int qib) => qib > 0 ? buyBlock / qib : buyBlock;

    // 1) Barcha reviziyalar ro'yxati (yangi -> eski)
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _db.Revisions
            .Include(r => r.Items)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        return Ok(list.Select(BuildSummary).ToList());
    }

    // 2) Bitta reviziya (qatorlari bilan)
    [HttpGet("{id}")]
    public async Task<IActionResult> GetOne(int id)
    {
        var r = await _db.Revisions.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        return Ok(BuildDetail(r));
    }

    // 3) Ochiq (Draft) reviziya bo'lsa qaytaradi (davom ettirish uchun)
    [HttpGet("current")]
    public async Task<IActionResult> Current()
    {
        var r = await _db.Revisions.Include(x => x.Items)
            .Where(x => x.Status == "Draft")
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
        if (r == null) return NoContent();
        return Ok(BuildDetail(r));
    }

    // 4) Yangi reviziya boshlash — barcha mahsulotlarni snapshot qiladi.
    //    Ochiq draft bo'lsa yangisini ochmaydi, mavjudini qaytaradi.
    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] RevisionStartRequest? req)
    {
        var existing = await _db.Revisions.Include(x => x.Items)
            .Where(x => x.Status == "Draft")
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
        if (existing != null) return Ok(BuildDetail(existing));

        var products = await _db.Products.OrderBy(p => p.Name).ToListAsync();
        var rev = new Revision
        {
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = req?.UserId,
            CreatedByName = req?.UserName ?? "",
            Status = "Draft",
            Note = req?.Note,
            Items = products.Select(p => new RevisionItem
            {
                ProductId = p.Id,
                ProductName = p.Name,
                Unit = p.Unit,
                QuantityInBlock = p.QuantityInBlock,
                SystemQty = p.TotalPieces,
                CountedQty = p.TotalPieces,
                IsCounted = false,
                BuyPriceBlock = p.BuyPriceBlock
            }).ToList()
        };
        _db.Revisions.Add(rev);
        await _db.SaveChangesAsync();
        return Ok(BuildDetail(rev));
    }

    // 5) Sanoq natijalarini saqlash (draft, hali qo'llanmaydi)
    [HttpPut("{id}/items")]
    public async Task<IActionResult> SaveItems(int id, [FromBody] RevisionSaveRequest? req)
    {
        var rev = await _db.Revisions.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (rev == null) return NotFound();
        if (rev.Status != "Draft") return BadRequest("Yakunlangan reviziyani o'zgartirib bo'lmaydi.");

        if (req?.Items != null)
        {
            foreach (var line in req.Items)
            {
                var it = rev.Items.FirstOrDefault(x => x.ProductId == line.ProductId);
                if (it == null) continue;
                it.CountedQty = line.CountedQty;
                it.IsCounted = line.IsCounted;
            }
        }
        if (req?.Note != null) rev.Note = req.Note;
        await _db.SaveChangesAsync();
        return Ok(BuildDetail(rev));
    }

    // 6) Bitta mahsulot sonini reviziya ichida tez saqlash
    [HttpPost("{id}/count")]
    public async Task<IActionResult> CountOne(int id, [FromBody] RevisionCountOneRequest req)
    {
        var rev = await _db.Revisions.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (rev == null) return NotFound();
        if (rev.Status != "Draft") return BadRequest("Yakunlangan reviziyani o'zgartirib bo'lmaydi.");
        var it = rev.Items.FirstOrDefault(x => x.ProductId == req.ProductId);
        if (it == null) return NotFound();
        it.CountedQty = req.CountedQty;
        it.IsCounted = true;
        await _db.SaveChangesAsync();
        return Ok(BuildSummary(rev));
    }

    // 7) Reviziyani yakunlash — sanalgan mahsulotlar sonini haqiqatga tenglaydi
    [HttpPost("{id}/complete")]
    public async Task<IActionResult> Complete(int id, [FromBody] RevisionCompleteRequest? req)
    {
        var rev = await _db.Revisions.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (rev == null) return NotFound();
        if (rev.Status != "Draft") return BadRequest("Bu reviziya allaqachon yakunlangan.");

        bool onlyCounted = req?.OnlyCounted ?? true;
        foreach (var it in rev.Items)
        {
            if (onlyCounted && !it.IsCounted) continue; // sanalmaganga tegilmaydi
            var product = await _db.Products.FindAsync(it.ProductId);
            if (product == null) continue;
            product.TotalPieces = it.CountedQty;
        }
        rev.Status = "Completed";
        rev.CompletedAt = DateTime.UtcNow;
        if (req?.Note != null) rev.Note = req.Note;
        await _db.SaveChangesAsync();
        return Ok(BuildDetail(rev));
    }

    // 8) Draft reviziyani o'chirish (yakunlangan o'chirilmaydi)
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var rev = await _db.Revisions.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (rev == null) return NotFound();
        if (rev.Status == "Completed") return BadRequest("Yakunlangan reviziyani o'chirib bo'lmaydi (tarix saqlanadi).");
        _db.RevisionItems.RemoveRange(rev.Items);
        _db.Revisions.Remove(rev);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // 9) Tez tuzatish — bitta mahsulot sonini to'g'ridan-to'g'ri o'zgartirish
    [HttpPost("quick-adjust")]
    public async Task<IActionResult> QuickAdjust([FromBody] QuickAdjustRequest req)
    {
        var product = await _db.Products.FindAsync(req.ProductId);
        if (product == null) return NotFound();
        product.TotalPieces = req.NewQty;
        await _db.SaveChangesAsync();
        return Ok(product);
    }

    // 10) Diqqat talab qiladigan mahsulotlar (minus yoki 0 qoldiq)
    [HttpGet("negatives")]
    public async Task<IActionResult> Negatives()
    {
        var products = await _db.Products
            .Where(p => p.TotalPieces <= 0)
            .OrderBy(p => p.TotalPieces)
            .ToListAsync();
        return Ok(products.Select(p => new
        {
            p.Id, p.Name, p.Unit, p.TotalPieces,
            IsNegative = p.TotalPieces < 0
        }));
    }

    // 11) Umumiy statistika (nechta reviziya, oxirgi sana, jami kamomad/ortiqcha)
    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var revs = await _db.Revisions.Include(r => r.Items).ToListAsync();
        var completed = revs.Where(r => r.Status == "Completed").ToList();
        double totalShortage = 0, totalSurplus = 0;
        foreach (var r in completed)
            foreach (var it in r.Items.Where(i => i.IsCounted))
            {
                int diff = it.CountedQty - it.SystemQty;
                double val = diff * PiecePrice(it.BuyPriceBlock, it.QuantityInBlock);
                if (diff < 0) totalShortage += -val;
                else if (diff > 0) totalSurplus += val;
            }
        return Ok(new
        {
            TotalRevisions = revs.Count,
            Completed = completed.Count,
            Drafts = revs.Count(r => r.Status == "Draft"),
            LastDate = revs.OrderByDescending(r => r.CreatedAt).FirstOrDefault()?.CreatedAt,
            TotalShortageValue = Math.Round(totalShortage),
            TotalSurplusValue = Math.Round(totalSurplus)
        });
    }

    // ── Yordamchilar ──
    private object BuildSummary(Revision r)
    {
        double shortageVal = 0, surplusVal = 0;
        int shortageCount = 0, surplusCount = 0;
        foreach (var it in r.Items.Where(i => i.IsCounted))
        {
            int diff = it.CountedQty - it.SystemQty;
            double val = diff * PiecePrice(it.BuyPriceBlock, it.QuantityInBlock);
            if (diff < 0) { shortageVal += -val; shortageCount++; }
            else if (diff > 0) { surplusVal += val; surplusCount++; }
        }
        return new
        {
            r.Id,
            r.CreatedAt,
            r.CompletedAt,
            r.CreatedByName,
            r.Status,
            r.Note,
            TotalItems = r.Items.Count,
            CountedItems = r.Items.Count(i => i.IsCounted),
            ShortageCount = shortageCount,
            SurplusCount = surplusCount,
            ShortageValue = Math.Round(shortageVal),
            SurplusValue = Math.Round(surplusVal),
            NetValue = Math.Round(surplusVal - shortageVal)
        };
    }

    private object BuildDetail(Revision r)
    {
        var items = r.Items.OrderBy(i => i.ProductName).Select(it =>
        {
            int diff = it.CountedQty - it.SystemQty;
            double val = Math.Round(diff * PiecePrice(it.BuyPriceBlock, it.QuantityInBlock));
            return new
            {
                it.Id,
                it.ProductId,
                it.ProductName,
                it.Unit,
                it.QuantityInBlock,
                it.SystemQty,
                it.CountedQty,
                it.IsCounted,
                it.BuyPriceBlock,
                Difference = diff,
                DiffValue = val
            };
        }).ToList();

        return new
        {
            r.Id,
            r.CreatedAt,
            r.CompletedAt,
            r.CreatedByName,
            r.CreatedByUserId,
            r.Status,
            r.Note,
            Items = items,
            TotalItems = r.Items.Count,
            CountedItems = r.Items.Count(i => i.IsCounted)
        };
    }
}

public class RevisionStartRequest
{
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Note { get; set; }
}

public class RevisionSaveRequest
{
    public string? Note { get; set; }
    public List<RevisionSaveLine> Items { get; set; } = new();
}

public class RevisionSaveLine
{
    public int ProductId { get; set; }
    public int CountedQty { get; set; }
    public bool IsCounted { get; set; }
}

public class RevisionCountOneRequest
{
    public int ProductId { get; set; }
    public int CountedQty { get; set; }
}

public class RevisionCompleteRequest
{
    public bool OnlyCounted { get; set; } = true;
    public string? Note { get; set; }
}

public class QuickAdjustRequest
{
    public int ProductId { get; set; }
    public int NewQty { get; set; }
    public string? UserName { get; set; }
    public string? Note { get; set; }
}
