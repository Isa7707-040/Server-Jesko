using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products { get; set; }
    public DbSet<ProductBarcode> ProductBarcodes { get; set; }
    public DbSet<Client> Clients { get; set; }
    public DbSet<DebtPayment> DebtPayments { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<Stock> Stocks { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<OnlineCustomer> OnlineCustomers { get; set; }
    // Oluv (kirim) moduli
    public DbSet<Supplier> Suppliers { get; set; }
    public DbSet<Purchase> Purchases { get; set; }
    public DbSet<PurchaseItem> PurchaseItems { get; set; }
    public DbSet<SupplierPayment> SupplierPayments { get; set; }
    // Qaytarish (vozvrat) moduli
    public DbSet<Return> Returns { get; set; }
    public DbSet<ReturnItem> ReturnItems { get; set; }
    // Sinxron (offline -> server) operatsiyalari tarixi (idempotentlik uchun)
    public DbSet<SyncOperation> SyncOperations { get; set; }
    // Umumiy sozlamalar (kalit/qiymat)
    public DbSet<Setting> Settings { get; set; }
    // Reviziya (inventarizatsiya) moduli
    public DbSet<Revision> Revisions { get; set; }
    public DbSet<RevisionItem> RevisionItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProductBarcode>()
            .HasOne(b => b.Product)
            .WithMany(p => p.Barcodes)
            .HasForeignKey(b => b.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProductBarcode>()
            .HasIndex(b => b.Code)
            .IsUnique();

        modelBuilder.Entity<DebtPayment>()
            .HasOne(p => p.Client)
            .WithMany()
            .HasForeignKey(p => p.ClientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Order>()
            .HasOne(o => o.User)
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Order>()
            .HasOne(o => o.Cashier)
            .WithMany()
            .HasForeignKey(o => o.CashierId)
            .OnDelete(DeleteBehavior.Restrict);

        // ⭐ YANGI: Online mijoz bog'lanishi
        modelBuilder.Entity<Order>()
            .HasOne(o => o.OnlineCustomer)
            .WithMany()
            .HasForeignKey(o => o.OnlineCustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<User>().HasData(new User
        {
            Id = 1,
            FullName = "Administrator",
            Username = "admin",
            Password = "admin123",
            Role = "Admin",
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1)
        });

        modelBuilder.Entity<Purchase>()
            .HasOne(p => p.Supplier)
            .WithMany()
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PurchaseItem>()
            .HasOne(i => i.Purchase)
            .WithMany(p => p.Items)
            .HasForeignKey(i => i.PurchaseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PurchaseItem>()
            .HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SupplierPayment>()
            .HasOne(p => p.Supplier)
            .WithMany()
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SyncOperation>()
            .HasIndex(o => o.OperationId)
            .IsUnique();

        modelBuilder.Entity<Setting>()
            .HasIndex(s => s.Key)
            .IsUnique();

        // -- QAYTARISH (VOZVRAT) bog'lanishlari --
        modelBuilder.Entity<Return>()
            .HasOne(r => r.Order)
            .WithMany()
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Return>()
            .HasOne(r => r.Cashier)
            .WithMany()
            .HasForeignKey(r => r.CashierId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ReturnItem>()
            .HasOne(i => i.Return)
            .WithMany(r => r.Items)
            .HasForeignKey(i => i.ReturnId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ReturnItem>()
            .HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // -- REVIZIYA (INVENTARIZATSIYA) bog'lanishi --
        modelBuilder.Entity<RevisionItem>()
            .HasOne(i => i.Revision)
            .WithMany(r => r.Items)
            .HasForeignKey(i => i.RevisionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
