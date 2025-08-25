using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RecoPaperTrader;

[Table("PaperAccount", Schema = "dbo")]
public class PaperAccount
{
    public int Id { get; set; }
    [MaxLength(100)] public string AccountName { get; set; } = "Paper-Default";
    public DateTime CreatedUtc { get; set; }
    public decimal Equity { get; set; }
    public decimal Cash { get; set; }
}
