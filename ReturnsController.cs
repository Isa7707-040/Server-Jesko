using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Controllers;

// ===================================================================
//  QAYTARISH (VOZVRAT) KONTROLLERI
//  Kassir "Mening sotuvlarim" dan chekni topib, mahsulotni (to'liq yoki
//  qisman) qaytaradi. Bunda:
//   - qaytgan tovar OMBORGA qayta qo'shiladi (TotalPieces += qty),
//   - asl buyurtma O'ZGARMAYDI, faqat har qatordagi ReturnedQuantity oshadi
//     va Order.RefundedSum yangilanadi (chek qayta chiqarishda ko'rinishi uchun),
//   - qaytarilgan pul KASSADAN chiqadi - shuning uchun qaytarish O'Z sanasi va
//     kassiri bilan alohida yoziladi (kunlik tushum shu kundan kamayadi),
//   - chegirma va qarz ulushi hisobga olinadi (hisob-kitob xato chiqmasligi uchun).
// ===================================================================
[ApiController]
[Route("api/[controller]")]
public class ReturnsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ReturnsController(AppDbContext db) => _db = db;

    // Bitta buyurtma bo'yicha barcha qaytarishlar (modalda "avval nima qaytarilgan"ni ko'rsatish uchun)
    [HttpGet("order/{orderId}")]
    public async Task<IActionResult> GetByOrder(int orderId)
    {
        var list = await _db.Returns
            .Where(r => r.OrderId == orderId)
            .Include(r => r.Cashier)
            .Include(r => r.Items).ThenInclude(i => i.Product)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        return Ok(list);
    }

    // Sana oralig'idagi qaytarishlar (kassir kunlik kassa solishtiruvi / admin statistikasi uchun).
    // cashierId berilsa - faqat o'sha kassir bajargan qaytarishlar.
    [HttpGet]
    public async Task<IActionResult> GetHistory([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? cashierId)
    {
        var query = _db.Returns
            .Include(r => r.Cashier)
            .Include(r => r.Items).ThenInclude(i => i.Product)
            .AsQueryable();

        if (from.HasValue) query = query.Where(r => r.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(r => r.CreatedAt <= to.Value.AddDays(1));
        if (cashierId.HasValue) query = query.Where(r => r.CashierId == cashierId.Value);

        var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
        return Ok(list);
    }

    // Qaytarishni bajarish. Hammasi bitta TRANZAKSIYADA - yo to'liq bajariladi, yo umuman o'zgarmaydi.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ReturnRequest req)
    {
        if (req.Items == null || req.Items.Count == 0)
            return BadRequest("Qaytariladigan mahsulot tanlanmadi.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var order = await _db.Orders
                .Include(o => o.Client)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(o => o.Id == req.OrderId);

            if (order == null) return NotFound("Buyurtma topilmadi.");
            if (order.Status == "Pending")
                return BadRequest("To'lanmagan (kutilayotgan) buyurtmadan qaytarib bo'lmaydi.");
            if (order.Status == "Cancelled")
                return BadRequest("Bekor qilingan buyurtmadan qaytarib bo'lmaydi.");

            // Kassir ID xavfsizligi: 0 yoki mavjud bo'lmagan xodim bo'lsa - null (tashqi kalit xatosi bo'lmasin).
            int? cashierId = (req.CashierId.HasValue && req.CashierId.Value > 0) ? req.CashierId : null;
            if (cashierId.HasValue && !await _db.Users.AnyAsync(u => u.Id == cashierId.Value))
                cashierId = null;

            // -- 1) Tekshiruv (hech narsani o'zgartirmasdan) --
            var applied = new List<(OrderItem oi, int qty)>();
            foreach (var reqItem in req.Items)
            {
                if (reqItem.Quantity <= 0) continue;
                var oi = order.Items.FirstOrDefault(x => x.Id == reqItem.OrderItemId);
                if (oi == null) return BadRequest($"Buyurtma qatori topilmadi (ID {reqItem.OrderItemId}).");
                int remaining = oi.Quantity - oi.ReturnedQuantity;
                if (reqItem.Quantity > remaining)
                    return BadRequest($"'{oi.Product?.Name}' uchun qaytarish miqdori noto'g'ri. Qolgan: {remaining} dona, so'ralgan: {reqItem.Quantity} dona.");
                applied.Add((oi, reqItem.Quantity));
            }
            if (applied.Count == 0) return BadRequest("Qaytariladigan mahsulot tanlanmadi.");

            // -- 2) Qaytarish qiymati (asl, chegirmagacha narxlar bo'yicha) --
            double refundValue = 0;
            var retItems = new List<ReturnItem>();
            foreach (var (oi, qty) in applied)
            {
                if (oi.Product != null) oi.Product.TotalPieces += qty;   // OMBORGA qaytadi
                oi.ReturnedQuantity += qty;
                refundValue += qty * oi.Price;
                retItems.Add(new ReturnItem
                {
                    ProductId = oi.ProductId,
                    OrderItemId = oi.Id,
                    Quantity = qty,
                    Price = oi.Price
                });
            }

            // -- 3) Chegirma ulushi: mijoz aslida shuncha to'lagan --
            double originalItemsSum = order.Items.Sum(i => i.Quantity * i.Price);
            double discountRatio = originalItemsSum > 0.5 ? order.TotalSum / originalItemsSum : 1.0;
            double refundAfterDiscount = Math.Round(refundValue * discountRatio);

            // -- 4) To'langan ulush: qarzli sotuvda mijoz to'liq to'lamagan bo'lishi mumkin --
            double paidRatio = order.TotalSum > 0.5 ? Math.Min(1.0, order.PaidSum / order.TotalSum) : 1.0;
            double cashRefund = Math.Round(refundAfterDiscount * paidRatio);   // kassadan chiqadigan pul
            double debtReduced = Math.Round(refundAfterDiscount - cashRefund); // mijoz qarzidan ayriladi

            // -- 5) Naqd/plastik taqsimoti - buyurtma qanday to'langan bo'lsa, shunga proporsional --
            double orderCash = (order.CashAmount > 0.5 || order.CardAmount > 0.5)
                ? order.CashAmount
                : (order.PaymentType == "Card" ? 0 : order.PaidSum);
            double orderCard = (order.CashAmount > 0.5 || order.CardAmount > 0.5)
                ? order.CardAmount
                : (order.PaymentType == "Card" ? order.PaidSum : 0);
            double paidTotal = orderCash + orderCard;

            double cashPart, cardPart;
            string retType;
            if (paidTotal > 0.5 && cashRefund > 0.5)
            {
                cardPart = Math.Round(cashRefund * (orderCard / paidTotal));
                cashPart = Math.Round(cashRefund - cardPart);
                retType = (cardPart > 0.5 && cashPart > 0.5) ? "Mixed" : (cardPart > 0.5 ? "Card" : "Cash");
            }
            else
            {
                cashPart = cashRefund;
                cardPart = 0;
                retType = "Cash";
            }

            // -- 6) Mijoz qarzini kamaytirish (qarzli qism qaytsa) --
            if (debtReduced > 0.5 && order.ClientId.HasValue && order.Client != null)
            {
                order.Client.DebtBalance = Math.Round(order.Client.DebtBalance - debtReduced);
                if (order.Client.DebtBalance < 1) order.Client.DebtBalance = 0;
            }

            // -- 7) Buyurtmaning jami qaytarilgan qiymati (chek/tarix uchun) --
            order.RefundedSum = Math.Round(order.RefundedSum + refundAfterDiscount);

            var ret = new Return
            {
                OrderId = order.Id,
                CashierId = cashierId,
                TotalSum = refundAfterDiscount,
                CashAmount = cashPart,
                CardAmount = cardPart,
                DebtReduced = debtReduced,
                PaymentType = retType,
                CreatedAt = DateTime.UtcNow,
                Items = retItems
            };
            _db.Returns.Add(ret);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            var created = await _db.Returns
                .Include(r => r.Cashier)
                .Include(r => r.Items).ThenInclude(i => i.Product)
                .FirstAsync(r => r.Id == ret.Id);
            return Ok(created);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            // Aniq sababni ko'rsatamiz (ichki xatolar zanjiri bilan).
            string detail = ex.Message;
            var inner = ex.InnerException;
            while (inner != null) { detail += "  |  " + inner.Message; inner = inner.InnerException; }
            return StatusCode(500, "Qaytarishda xatolik: " + detail);
        }
    }
}

public class ReturnRequest
{
    public int OrderId { get; set; }
    public int? CashierId { get; set; }
    public List<ReturnItemRequest> Items { get; set; } = new();
}

public class ReturnItemRequest
{
    public int OrderItemId { get; set; }
    public int Quantity { get; set; }
}
