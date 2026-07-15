namespace StoreSystem.Api.Models;

public class Order
{
    public int Id { get; set; }
    public int? ClientId { get; set; }
    public Client? Client { get; set; }
    public int? OnlineCustomerId { get; set; }        // ⭐ YANGI: Online mijoz
    public OnlineCustomer? OnlineCustomer { get; set; } // ⭐ YANGI: Online mijoz reference
    public int? UserId { get; set; }           // Qaysi sotuvchidan kelgan
    public User? User { get; set; }
    public int? CashierId { get; set; }         // Qaysi kassir to'lovni qabul qilgan
    public User? Cashier { get; set; }
    public double TotalSum { get; set; }
    public double PaidSum { get; set; }
    public string Status { get; set; } = "Pending"; // Pending | Paid | Debt
    public string PaymentType { get; set; } = "Cash"; // Cash | Card | Mixed
    // Aralash to'lovda (Mixed) naqd va plastik qismlari alohida saqlanadi.
    // Toza naqd/plastik to'lovda ham to'ldiriladi (CashAmount+CardAmount = PaidSum).
    public double CashAmount { get; set; }
    public double CardAmount { get; set; }
    // Shu buyurtmadan jami qaytarilgan (vozvrat) qiymati - chegirma hisobga olingan.
    // Chek/tarix "sof (yakuniy)" summani ko'rsatishi uchun ishlatiladi. 0 bo'lsa qaytarish yo'q.
    public double RefundedSum { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
    public double Price { get; set; }
    // Chegirmadan (skidka) OLDINGI asl narx. 0 bo'lsa chegirma qilinmagan.
    // Chek tarixdan qayta ochilganda ham chegirmali narxni ko'rsatish uchun saqlanadi.
    public double OriginalPrice { get; set; }
    // Shu qatordan qaytarilgan (vozvrat qilingan) dona soni (0 .. Quantity).
    public int ReturnedQuantity { get; set; }
}
