namespace StoreSystem.Api.Models;

// ════════════════════════════════════════════════════════
//  QAYTARISH (VOZVRAT)
//  Mijoz avval sotib olgan mahsulotni (to'liq yoki qisman) qaytarsa, shu
//  yozuv yaratiladi. Har bir qaytarish O'Z SANASIGA ega (CreatedAt) — shuning
//  uchun necha kun oldingi sotuv bugun qaytarilsa, bugungi kassa/tushumdan
//  ayriladi. Asl buyurtma (Order) va uning qatorlari (OrderItem) O'ZGARMAYDI;
//  faqat OrderItem.ReturnedQuantity va Order.RefundedSum yangilanadi — shunda
//  "avval nima olingan" va "nima qaytarilgan" ikkalasi ham aniq saqlanadi.
// ════════════════════════════════════════════════════════
public class Return
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public int? CashierId { get; set; }         // Qaytarishni amalga oshirgan kassir
    public User? Cashier { get; set; }
    public double TotalSum { get; set; }        // Qaytarilgan qiymat (chegirma hisobga olingan)
    public double CashAmount { get; set; }      // Kassadan naqd qaytarilgan qism
    public double CardAmount { get; set; }      // Plastik orqali qaytarilgan qism
    public double DebtReduced { get; set; }     // Mijoz qarzidan kamaytirilgan qism (qarzli sotuvda)
    public string PaymentType { get; set; } = "Cash"; // Cash | Card | Mixed
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<ReturnItem> Items { get; set; } = new();
}

public class ReturnItem
{
    public int Id { get; set; }
    public int ReturnId { get; set; }
    public Return? Return { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int OrderItemId { get; set; }        // Asl buyurtma qatori (qaysi qatordan qaytarilgani)
    public int Quantity { get; set; }           // Qaytarilgan dona soni
    public double Price { get; set; }           // Sotuvdagi dona narxi (chegirmagacha)
}
