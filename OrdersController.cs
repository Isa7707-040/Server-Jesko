using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _db;
    public OrdersController(AppDbContext db) => _db = db;

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        var orders = await _db.Orders
            .Where(o => o.Status == "Pending")
            .Include(o => o.Client)
            .Include(o => o.User)
            .Include(o => o.Cashier)
            .Include(o => o.OnlineCustomer) // ⭐ YANGI
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
        return Ok(orders);
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var query = _db.Orders
            .Where(o => o.Status != "Pending")
            .Include(o => o.Client)
            .Include(o => o.User)
            .Include(o => o.Cashier)
            .Include(o => o.OnlineCustomer) // ⭐ YANGI
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .AsQueryable();
        if (from.HasValue) query = query.Where(o => o.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(o => o.CreatedAt <= to.Value.AddDays(1));
        var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
        return Ok(orders);
    }

    [HttpGet("client/{clientId}")]
    public async Task<IActionResult> GetByClient(int clientId)
    {
        var orders = await _db.Orders
            .Where(o => o.ClientId == clientId)
            .Include(o => o.Client)
            .Include(o => o.User)
            .Include(o => o.Cashier)
            .Include(o => o.OnlineCustomer) // ⭐ YANGI
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
        return Ok(orders);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var fromDate = from ?? DateTime.UtcNow.Date;
        var toDate = to?.AddDays(1) ?? DateTime.UtcNow.Date.AddDays(1);

        var orders = await _db.Orders
            .Where(o => o.Status == "Paid" && o.CreatedAt >= fromDate && o.CreatedAt < toDate)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .ToListAsync();

        double totalRevenue = orders.Sum(o => o.PaidSum);
        double totalCost = orders.SelectMany(o => o.Items)
            .Sum(i => i.Quantity * (i.Product?.BuyPriceBlock ?? 0) / (i.Product?.QuantityInBlock ?? 1));
        double cashTotal = orders.Sum(CashPart);
        double cardTotal = orders.Sum(CardPart);

        var periodReturns = await _db.Returns
            .Where(r => r.CreatedAt >= fromDate && r.CreatedAt < toDate)
            .Include(r => r.Items).ThenInclude(i => i.Product)
            .ToListAsync();

        double returnCash = periodReturns.Sum(r => r.CashAmount);
        double returnCard = periodReturns.Sum(r => r.CardAmount);
        double returnRevenue = returnCash + returnCard;
        double returnCost = periodReturns.SelectMany(r => r.Items)
            .Sum(i => i.Quantity * (i.Product?.BuyPriceBlock ?? 0) / (i.Product?.QuantityInBlock ?? 1));

        totalRevenue -= returnRevenue;
        totalCost -= returnCost;
        cashTotal -= returnCash;
        cardTotal -= returnCard;
        double profit = totalRevenue - totalCost;

        return Ok(new
        {
            TotalRevenue = totalRevenue,
            TotalCost = totalCost,
            Profit = profit,
            CashTotal = cashTotal,
            CardTotal = cardTotal,
            OrderCount = orders.Count,
            DebtTotal = await _db.Clients.SumAsync(c => c.DebtBalance),
            ReturnTotal = periodReturns.Sum(r => r.TotalSum),
            ReturnCount = periodReturns.Count
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] OrderRequest req)
    {
        if (req.Items == null || req.Items.Count == 0)
            return BadRequest("Savat bosh.");

        var stockError = await CheckStockAsync(req.Items.Select(i => (i.ProductId, i.Quantity)));
        if (stockError != null) return BadRequest(stockError);

        var order = new Order
        {
            ClientId = req.ClientId,
            OnlineCustomerId = req.OnlineCustomerId, // ⭐ YANGI
            UserId = req.UserId,
            TotalSum = req.TotalSum,
            PaidSum = 0,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow,
            Items = req.Items.Select(i => new OrderItem
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                Price = i.Price
            }).ToList()
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        var created = await _db.Orders
            .Include(o => o.Client)
            .Include(o => o.User)
            .Include(o => o.Cashier)
            .Include(o => o.OnlineCustomer) // ⭐ YANGI
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstAsync(o => o.Id == order.Id);
        return Ok(created);
    }

    [HttpPost("quicksell")]
    public async Task<IActionResult> QuickSell([FromBody] QuickSellRequest req)
    {
        if (req.Items == null || req.Items.Count == 0)
            return BadRequest("Savat bosh.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            string? opId = string.IsNullOrWhiteSpace(req.OperationId) ? null : req.OperationId!.Trim();
            if (opId != null)
            {
                var dup = await _db.SyncOperations.FirstOrDefaultAsync(s => s.OperationId == opId);
                if (dup != null)
                {
                    await tx.RollbackAsync();
                    return Ok(new { duplicate = true, message = "Bu sotuv allaqachon qabul qilingan." });
                }
            }

            double total = 0;
            foreach (var item in req.Items)
            {
                var product = await _db.Products.FindAsync(item.ProductId);
                if (product == null) return BadRequest($"Mahsulot topilmadi: {item.ProductId}");
                if (item.Quantity <= 0) return BadRequest($"Miqdor notogri: {product.Name}");
                total += item.Quantity * item.Price;
            }

            var stockError = await CheckStockAsync(req.Items.Select(i => (i.ProductId, i.Quantity)));
            if (stockError != null) return BadRequest(stockError);

            var (qsCash, qsCard, qsType) = SplitPayment(total, req.PaymentType, req.CashAmount, req.CardAmount);

            var order = new Order
            {
                ClientId = null,
                OnlineCustomerId = null, // ⭐ QuickSell'da online customer yo'q
                UserId = null,
                CashierId = req.CashierId,
                TotalSum = total,
                PaidSum = total,
                Status = "Paid",
                PaymentType = qsType,
                CashAmount = qsCash,
                CardAmount = qsCard,
                CreatedAt = req.ClientCreatedAt ?? DateTime.UtcNow,
                Items = req.Items.Select(i => new OrderItem
                {
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    Price = i.Price
                }).ToList()
            };
            _db.Orders.Add(order);

            foreach (var item in req.Items)
            {
                var product = await _db.Products.FindAsync(item.ProductId);
                if (product != null) product.TotalPieces -= item.Quantity;
            }
            await _db.SaveChangesAsync();

            if (opId == null)
            {}
            else
            {
                _db.SyncOperations.Add(new SyncOperation
                {
                    OperationId = opId,
                    Type = "QuickSell",
                    EntityId = order.Id,
                    Amount = total,
                    AppliedAmount = total,
                    ClientCreatedAt = req.ClientCreatedAt ?? DateTime.UtcNow,
                    AppliedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }
            await tx.CommitAsync();

            var created = await _db.Orders
                .Include(o => o.User)
                .Include(o => o.Cashier)
                .Include(o => o.OnlineCustomer) // ⭐ YANGI
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .FirstAsync(o => o.Id == order.Id);
            return Ok(created);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return StatusCode(500, "Sotuvda xatolik: " + ex.Message);
        }
    }

    [HttpPost("{id}/pay")]
    public async Task<IActionResult> Pay(int id, [FromBody] PayRequest req)
    {
        var order = await _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .Include(o => o.Client)
            .Include(o => o.OnlineCustomer) // ⭐ YANGI
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) return NotFound();
        if (order.Status != "Pending") return BadRequest("Buyurtma allaqachon tolangan.");

        var payStockError = await CheckStockAsync(order.Items.Select(i => (i.ProductId, i.Quantity)));
        if (payStockError != null) return BadRequest(payStockError);

        foreach (var item in order.Items)
        {
            if (item.Product != null)
                item.Product.TotalPieces -= item.Quantity;
        }

        order.PaidSum = req.PaidSum;
        var (cashPart, cardPart, normType) = SplitPayment(req.PaidSum, req.PaymentType, req.CashAmount, req.CardAmount);
        order.PaymentType = normType;
        order.CashAmount = cashPart;
        order.CardAmount = cardPart;
        if (req.CashierId.HasValue && req.CashierId.Value > 0)
            order.CashierId = req.CashierId.Value;

        double debt = Math.Round(order.TotalSum - req.PaidSum);
        if (debt > 0.5)
        {
            order.Status = "Debt";
            if (order.ClientId.HasValue && order.Client != null)
                order.Client.DebtBalance = Math.Round(order.Client.DebtBalance + debt);
        }
        else
        {
            order.Status = "Paid";
        }

        double debtPay = Math.Round(req.DebtPaymentSum);
        if (debtPay > 0 && order.ClientId.HasValue && order.Client != null)
        {
            double applied = Math.Min(debtPay, order.Client.DebtBalance);
            if (applied > 0)
            {
                order.Client.DebtBalance = Math.Round(order.Client.DebtBalance - applied);
                if (order.Client.DebtBalance < 1) order.Client.DebtBalance = 0;

                string cashierName = "";
                if (order.CashierId.HasValue)
                {
                    var cashier = await _db.Users.FindAsync(order.CashierId.Value);
                    cashierName = cashier?.FullName ?? "";
                }

                _db.DebtPayments.Add(new DebtPayment
                {
                    ClientId = order.ClientId.Value,
                    Amount = applied,
                    RemainingAfter = order.Client.DebtBalance,
                    PaidAt = DateTime.UtcNow,
                    Note = $"Kassada tolandi (chek #{order.Id}" +
                           (string.IsNullOrEmpty(cashierName) ? ")" : $", kassir: {cashierName})")
                });
            }
        }

        await _db.SaveChangesAsync();

        var paid = await _db.Orders
            .Include(o => o.Client)
            .Include(o => o.User)
            .Include(o => o.Cashier)
            .Include(o => o.OnlineCustomer) // ⭐ YANGI
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstAsync(o => o.Id == order.Id);
        return Ok(paid);
    }

    [HttpPost("{id}/discount")]
    public async Task<IActionResult> ApplyDiscount(int id, [FromBody] DiscountRequest req)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) return NotFound();
        if (order.Status != "Pending")
            return BadRequest("Chegirma faqat pending buyurtmaga qollanadi.");

        if (req.Items != null && req.Items.Count > 0)
        {
            foreach (var itemReq in req.Items)
            {
                var item = order.Items.FirstOrDefault(i => i.Id == itemReq.OrderItemId);
                if (item != null)
                {
                    // Asl narxni (chegirmadan oldingi) saqlab qolamiz - chek tarixdan
                    // qayta ochilganda chegirmali narxni ko'rsatish uchun.
                    if (item.OriginalPrice < 0.5) item.OriginalPrice = item.Price;
                    item.Price = Math.Max(0, Math.Round(itemReq.NewPrice));
                }
            }
            order.TotalSum = Math.Round(order.Items.Sum(i => i.Quantity * i.Price));
        }
        else
        {
            double original = Math.Round(order.Items.Sum(i => i.Quantity * i.Price));
            double newTotal = Math.Round(req.TotalSum);
            if (newTotal < 0) newTotal = 0;
            if (newTotal > original) newTotal = original;
            order.TotalSum = newTotal;
        }

        await _db.SaveChangesAsync();

        var updated = await _db.Orders
            .Include(o => o.Client)
            .Include(o => o.User)
            .Include(o => o.Cashier)
            .Include(o => o.OnlineCustomer) // ⭐ YANGI
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstAsync(o => o.Id == order.Id);
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Cancel(int id)
    {
        var order = await _db.Orders.FindAsync(id);
        if (order == null) return NotFound();
        if (order.Status != "Pending") return BadRequest("Faqat pending buyurtmalarni bekor qilish mumkin.");
        order.Status = "Cancelled";
        await _db.SaveChangesAsync();
        return Ok();
    }

    private async Task<string?> CheckStockAsync(IEnumerable<(int ProductId, int Quantity)> lines)
    {
        var grouped = lines
            .GroupBy(l => l.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(x => x.Quantity) });
        foreach (var g in grouped)
        {
            var product = await _db.Products.FindAsync(g.ProductId);
            if (product == null) return $"Mahsulot topilmadi (ID {g.ProductId}).";
            if (g.Qty <= 0) return $"Miqdor notogri:  {product.Name}";
            if (product.TotalPieces < g.Qty)
            {
                int available = Math.Max(0, product.TotalPieces);
                return $"{product.Name} omborda yetarli emas. Mavjud: {available}, soralgan: {g.Qty}. Aval kirim qiling.";
            }
        }
        return null;
    }

    private static (double cash, double card, string type) SplitPayment(
        double orderPortion, string? paymentType, double cashReq, double cardReq)
    {
        orderPortion = Math.Max(0, Math.Round(orderPortion));
        string t = (paymentType ?? "Cash").Trim();
        if (t.Equals("Mixed", StringComparison.OrdinalIgnoreCase))
        {
            double card = Math.Round(Math.Max(0, cardReq));
            if (card > orderPortion) card = orderPortion;
            double cash = orderPortion - card;
            if (cash <= 0.5 && card > 0.5) return (0, card, "Card");
            if (card <= 0.5 && cash > 0.5) return (cash, 0, "Cash");
            return (cash, card, "Mixed");
        }
        if (t.Equals("Card", StringComparison.OrdinalIgnoreCase))
            return (0, orderPortion, "Card");
        return (orderPortion, 0, "Cash");
    }

    private static double CashPart(Order o)
    {
        if (o.CashAmount > 0.5 || o.CardAmount > 0.5) return o.CashAmount;
        return o.PaymentType == "Card" ? 0 : o.PaidSum;
    }

    private static double CardPart(Order o)
    {
        if (o.CashAmount > 0.5 || o.CardAmount > 0.5) return o.CardAmount;
        return o.PaymentType == "Card" ? o.PaidSum : 0;
    }
}

public class OrderRequest
{
    public int? ClientId { get; set; }
    public int? OnlineCustomerId { get; set; } // ⭐ YANGI: Online mijoz
    public int? UserId { get; set; }
    public double TotalSum { get; set; }
    public List<OrderItemRequest> Items { get; set; } = new();
}

public class OrderItemRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public double Price { get; set; }
}

public class PayRequest
{
    public double PaidSum { get; set; }
    public string PaymentType { get; set; } = "Cash";
    public int? CashierId { get; set; }
    public double DebtPaymentSum { get; set; }
    public double CashAmount { get; set; }
    public double CardAmount { get; set; }
}

public class QuickSellRequest
{
    public int? CashierId { get; set; }
    public string PaymentType { get; set; } = "Cash";
    public double CashAmount { get; set; }
    public double CardAmount { get; set; }
    public string? OperationId { get; set; }
    public DateTime? ClientCreatedAt { get; set; }
    public List<OrderItemRequest> Items { get; set; } = new();
}

public class DiscountRequest
{
    public double TotalSum { get; set; }
    public List<DiscountItemRequest>? Items { get; set; }
}

public class DiscountItemRequest
{
    public int OrderItemId { get; set; }
    public double NewPrice { get; set; }
}
