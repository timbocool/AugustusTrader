using System.ComponentModel.DataAnnotations;

public class BalanceItem
{
    [Key]
    public string CurrencyId { get; set; }
    public string Currency { get; set; }
    public string Available { get; set; }
    public string Hold { get; set; }
}