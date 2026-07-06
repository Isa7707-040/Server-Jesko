namespace StoreSystem.Api.Models;

// Mijozning qarzini (qisman yoki to'liq) to'lashi tarixi.
// Masalan: 5 000 000 qarzdan 2 000 000 to'lansa — shu yozuv yaratiladi:
//   Amount = 2 000 000 (bu safar to'langan)
//   RemainingAfter = 3 000 000 (to'lovdan keyin qolgan qarz)
//   PaidAt = to'langan sana/vaqt
public class DebtPayment
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client? Client { get; set; }
    public double Amount { get; set; }          // shu to'lovda berilgan summa
    public double RemainingAfter { get; set; }  // to'lovdan keyin qolgan qarz
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }

    // ── KASSA SOLISHTIRUVI UCHUN (yangi) ─────────────────────────
    // Bu to'lovni QAYSI xodim qabul qilgani. Kassir kassada qabul qilsa
    // — kassirning Id'si; admin mobil ilovada ko'chada yig'ib, keyin
    // sinxron qilsa — adminning Id'si. Shu maydon orqali "Mening
    // sotuvlarim" va kunlik kassa hisobida bu pul ham hisobga olinadi.
    public int? CashierId { get; set; }

    // To'lov turi: "Cash" (naqd) yoki "Card" (plastik). Qarz odatda naqd
    // to'lanadi, lekin plastik orqali ham bo'lishi mumkin — kassa naqd/plastik
    // taqsimoti to'g'ri chiqishi uchun saqlaymiz. Default: naqd.
    public string PaymentType { get; set; } = "Cash";

    // To'lov qayerdan kelgani: "Kassa" (kassir kassada qabul qildi) yoki
    // "Mobil" (admin ko'chada yig'ib, sinxron orqali yubordi). Audit/ko'rsatish uchun.
    public string? Source { get; set; }
}
