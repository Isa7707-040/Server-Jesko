namespace StoreSystem.Api.Models;

// ===================================================================
//  REVIZIYA (INVENTARIZATSIYA)
//  Ombor sonlarini haqiqatga moslash uchun. Reviziya boshlanganda barcha
//  mahsulotlarning tizimdagi soni (SystemQty) snapshot qilinadi. Kassir/omborchi
//  haqiqiy sonni (CountedQty) kiritadi. "Yakunlash" bosilганda faqat sanalgan
//  mahsulotlar uchun Product.TotalPieces = CountedQty qilinadi.
//  Reviziyaning o'zi audit (tarix) sifatida saqlanadi.
// ===================================================================
public class Revision
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = "";
    public string Status { get; set; } = "Draft"; // Draft | Completed
    public string? Note { get; set; }
    public List<RevisionItem> Items { get; set; } = new();
}

public class RevisionItem
{
    public int Id { get; set; }
    public int RevisionId { get; set; }
    public Revision? Revision { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "dona";
    public int QuantityInBlock { get; set; } = 1;
    public int SystemQty { get; set; }        // reviziya boshlanganda tizimdagi son
    public int CountedQty { get; set; }       // sanab chiqilgan haqiqiy son
    public bool IsCounted { get; set; }       // bu mahsulot sanaldimi
    public double BuyPriceBlock { get; set; } // kamomad summasini hisoblash uchun
}
