using System.ComponentModel.DataAnnotations;

public class CrossMargin
{
    [Key]
    public int CrossMarginRecordId { get; set; }
    public bool SupportCrossMargin { get; set; }
    public int MaxLeverage { get; set; }
}
