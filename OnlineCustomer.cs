namespace StoreSystem.Api.Models;

/// <summary>
/// Online (mobil app orqali) mijozlar. Admin desktop'da qo'shiladi.
/// Har bir mijoz: Ism + Telefon + Parol (xeshlanmagan).
/// </summary>
public class OnlineCustomer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";           // Mijoz ismi
    public string PhoneNumber { get; set; } = "";    // Telefon raqami
    public string Password { get; set; } = "";       // Parol (xeshlanmagan — oddiy matn)
    public bool IsActive { get; set; } = true;       // Faol yoki faol emas
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
