// ===================================================================
//  ESLATMA: Bu fayl ataylab BO'SH qoldirilgan.
//
//  Ilgari bu yerda ortiqcha (dublikat) skidka endpointi bor edi:
//        POST api/Orders/{id}/discount-items
//  U OrdersController.cs dagi asosiy skidka endpointi bilan chalkashlik
//  keltirib chiqarardi (ikkalasi ham api/Orders route da).
//
//  Skidka mantig'i endi FAQAT bitta joyda - OrdersController.cs ichida:
//        POST api/Orders/{id}/discount   ->  ApplyDiscount(...)
//  U har bir mahsulotning chegirmali narxini (OrderItem.Price) saqlaydi
//  va TotalSum ni qayta hisoblaydi. Shuning uchun chekda mahsulot
//  chegirmali narxida ko'rinadi.
//
//  Desktop dastur ham aynan shu (discount) endpointini chaqiradi.
// ===================================================================
