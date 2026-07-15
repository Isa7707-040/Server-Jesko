using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OnlineCustomersController : ControllerBase
{
    private readonly AppDbContext _db;
    public OnlineCustomersController(AppDbContext db) => _db = db;

    /// <summary>
    /// Barcha faol online mijozlarni olish (Android ilova uchun login dropdown)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool includeInactive = false)
    {
        var query = _db.OnlineCustomers.AsQueryable();
        if (!includeInactive)
            query = query.Where(c => c.IsActive);
        
        var customers = await query
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.PhoneNumber })
            .ToListAsync();
        
        return Ok(customers);
    }

    /// <summary>
    /// Alohida online mijozni ID orqali olish
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var customer = await _db.OnlineCustomers.FindAsync(id);
        if (customer == null) return NotFound(new { message = "Mijoz topilmadi" });
        
        return Ok(new { customer.Id, customer.Name, customer.PhoneNumber, customer.IsActive });
    }

    /// <summary>
    /// Login — Android ilovadan
    /// Parol tekshiriladi (oddiy matn, xeshlanmagan)
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] OnlineLoginRequest req)
    {
        var customer = await _db.OnlineCustomers.FirstOrDefaultAsync(c =>
            c.Id == req.CustomerId && c.Password == req.Password && c.IsActive);

        if (customer == null)
            return Unauthorized(new { message = "Mijoz ID yoki parol noto'g'ri" });

        return Ok(new { customer.Id, customer.Name, customer.PhoneNumber });
    }

    /// <summary>
    /// Yangi online mijoz qo'shish (Desktop Admin panelidan)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOnlineCustomerRequest req)
    {
        // Telefon nomeri takrorlanmasligi tekshiriladi
        if (await _db.OnlineCustomers.AnyAsync(c => c.PhoneNumber == req.PhoneNumber))
            return BadRequest(new { message = "Bu telefon raqami allaqachon mavjud" });

        var customer = new OnlineCustomer
        {
            Name = req.Name,
            PhoneNumber = req.PhoneNumber,
            Password = req.Password, // TODO: Real holatda xeshlaash kerak (SHA256 / bcrypt)
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.OnlineCustomers.Add(customer);
        await _db.SaveChangesAsync();

        return Ok(new { customer.Id, customer.Name, customer.PhoneNumber });
    }

    /// <summary>
    /// Online mijozni tahrirlash (ism, telefon, parol)
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateOnlineCustomerRequest req)
    {
        var customer = await _db.OnlineCustomers.FindAsync(id);
        if (customer == null) return NotFound(new { message = "Mijoz topilmadi" });

        // Agar telefon o'zgartirilayotgan bo'lsa, takrorlanmasligi tekshiriladi
        if (!string.IsNullOrEmpty(req.PhoneNumber) && req.PhoneNumber != customer.PhoneNumber)
        {
            if (await _db.OnlineCustomers.AnyAsync(c => c.PhoneNumber == req.PhoneNumber && c.Id != id))
                return BadRequest(new { message = "Bu telefon raqami boshqa mijozda allaqachon mavjud" });
            customer.PhoneNumber = req.PhoneNumber;
        }

        if (!string.IsNullOrEmpty(req.Name))
            customer.Name = req.Name;

        if (!string.IsNullOrEmpty(req.Password))
            customer.Password = req.Password; // TODO: xeshlaash

        customer.IsActive = req.IsActive ?? customer.IsActive;

        await _db.SaveChangesAsync();
        return Ok(new { customer.Id, customer.Name, customer.PhoneNumber, customer.IsActive });
    }

    /// <summary>
    /// Online mijozni o'chirish (deactivate)
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var customer = await _db.OnlineCustomers.FindAsync(id);
        if (customer == null) return NotFound(new { message = "Mijoz topilmadi" });

        customer.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Mijoz o'chirildi" });
    }
}

/// <summary>
/// Login uchun request modeli
/// </summary>
public class OnlineLoginRequest
{
    public int CustomerId { get; set; }
    public string Password { get; set; } = "";
}

/// <summary>
/// Yangi mijoz qo'shish uchun request modeli
/// </summary>
public class CreateOnlineCustomerRequest
{
    public string Name { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string Password { get; set; } = "";
}

/// <summary>
/// Mijozni tahrirlash uchun request modeli
/// </summary>
public class UpdateOnlineCustomerRequest
{
    public string? Name { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Password { get; set; }
    public bool? IsActive { get; set; }
}
