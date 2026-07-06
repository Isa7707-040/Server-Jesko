using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Controllers;

// ===================================================================
//  KASSIR: KUTILAYOTGAN BUYURTMANI TAHRIRLASH
//  Bu ALOHIDA controller — mavjud OrdersController.cs ga UMUMAN
//  tegilmaydi (hech narsa buzilmaydi). Faqat YANGI endpoint qo'shadi:
//        PUT api/Orders/{id}/items
//
//  Muammo: mijoz keldi, sotuvchi savatga oldi, kassirga yubordi. Kassir
//  ochganda mijoz fikrini o'zgartiradi ("bu kerak emas", "bundan kam").
//  Avval kassir butun buyurtmani bekor qilib, sotuvchi qaytadan yig'ardi.
//  Endi kassir buyurtmani tahrirlaydi: sonini kamaytiradi/oshiradi yoki
//  mahsulotni butunlay olib tashlaydi va saqlaydi.
//
//  OMBOR HAQIDA MUHIM (nega bu 100% xatosiz):
//   Pending (kutilayotgan) buyurtma OMBORDAN mahsulot AYIRMAYDI — ombor
//   faqat to'lov (pay) yoki tezkor sotuv (quicksell) paytida kamayadi.
//   Shuning uchun bu yerda ombor bilan hech qanday ish qilinmaydi:
//     • olib tashlangan mahsulot avtomatik "omborda qoladi";
//     • qo'shilgan mahsulot to'lov paytida yakuniy songa qarab ayiriladi.
//   Bu ziddiyatsiz va ikki marta ayirish/qaytarish xatosi bo'lishi mumkin emas.
// ===================================================================
[ApiController]
[Route("api/Orders")]
public class OrdersEditController : ControllerBase
{
    private readonly AppDbContext _db;
    public OrdersEditController(AppDbContext db) => _db = db;

    [HttpPut("{id}/items")]
    public async Task<IActionResult> UpdateItems(int id, [FromBody] EditOrderItemsRequest req)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) return NotFound();
        if (order.Status != "Pending")
            return BadRequest("Faqat kutilayotgan (to'lanmagan) buyurtmani tahrirlash mumkin.");

        var items = (req?.Items ?? new List<EditOrderItemLine>())
            .Where(i => i.Quantity > 0)
            .ToList();

        if (items.Count == 0)
            return BadRequest("Buyurtmada kamida bitta mahsulot qolishi kerak. " +
                              "Butun buyurtmani bekor qilmoqchi bo'lsangiz, bekor qilish tugmasidan foydalaning.");

        // Ombor yetarliligini tekshiramiz. (Pending buyurtma stokni band qilmaydi,
        // shuning uchun faqat MAVJUDLIK tekshiriladi — haqiqiy ayirish to'lov paytida.)
        foreach (var g in items.GroupBy(i => i.ProductId)
                                .Select(x => new { ProductId = x.Key, Qty = x.Sum(v => v.Quantity) }))
        {
            var product = await _db.Products.FindAsync(g.ProductId);
            if (product == null) return BadRequest($"Mahsulot topilmadi (ID {g.ProductId}).");
            if (product.TotalPieces < g.Qty)
            {
                int available = Math.Max(0, product.TotalPieces);
                return BadRequest($"'{product.Name}' omborda yetarli emas. " +
                                  $"Mavjud: {available} dona, so'ralgan: {g.Qty} dona.");
            }
        }

        // Eski qatorlarni tozalab, yangilarini yozamiz (toza va ishonchli).
        // Narx (Price) mijoz talabiga ko'ra o'zgarmaydi — mavjud narx saqlanadi.
        _db.OrderItems.RemoveRange(order.Items);
        order.Items = items.Select(i => new OrderItem
        {
            ProductId = i.ProductId,
            Quantity = i.Quantity,
            Price = i.Price
        }).ToList();

        // Yangi jami — mahsulotlar summasi bo'yicha qayta hisoblanadi.
        order.TotalSum = Math.Round(items.Sum(i => i.Quantity * i.Price));

        await _db.SaveChangesAsync();

        var updated = await _db.Orders
            .Include(o => o.Client)
            .Include(o => o.User)
            .Include(o => o.Cashier)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstAsync(o => o.Id == order.Id);

        return Ok(updated);
    }
}

public class EditOrderItemsRequest
{
    public List<EditOrderItemLine> Items { get; set; } = new();
}

public class EditOrderItemLine
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public double Price { get; set; }
}
