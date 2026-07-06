namespace StoreSystem.Api.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "dona";
    public int QuantityInBlock { get; set; } = 1;
    public double BuyPriceBlock { get; set; }
    public double SellPriceBlock { get; set; }
    public double SellPricePiece { get; set; }
    public int TotalPieces { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Mahsulotga biriktirilgan barcha shtrix-kodlar (bir mahsulot — ko'p kod).
    public List<ProductBarcode> Barcodes { get; set; } = new();
}