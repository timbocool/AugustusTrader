using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("CurrencyData")]
public class CurrencyData
{
    [Key]
    public int Id { get; set; }

    public int CurrencyId { get; set; }

    [Required]
    public string? Name { get; set; }

    public string? Description { get; set; }

    public string? Type { get; set; }

    public float WithdrawalFee { get; set; }

    public int MinConf { get; set; }

    public string? DepositAddress { get; set; }

    public string? Blockchain { get; set; }

    public bool Delisted { get; set; }

    public string? TradingState { get; set; }

    public string? WalletState { get; set; }

    public string? WalletDepositState { get; set; }

    public string? WalletWithdrawalState { get; set; }

    public string? ParentChain { get; set; }

    public bool IsMultiChain { get; set; }

    public bool IsChildChain { get; set; }

    public bool SupportCollateral { get; set; }

    public bool SupportBorrow { get; set; }

    //CUSTOM FIELDS
    public DateTime AddedDateTime { get; set; }

    [NotMapped]
    public List<string>? ChildChains { get; set; }
}

//public class CurrencyAPI
//{
//      [JsonProperty("id")]
//    public int Id { get; set; }

//    [JsonProperty("name")]
//    public string Name { get; set; }

//    [JsonProperty("description")]
//    public string Description { get; set; }

//    [JsonProperty("type")]
//    public string Type { get; set; }

//    [JsonProperty("withdrawalFee")]
//    public string WithdrawalFee { get; set; }

//    [JsonProperty("minConf")]
//    public int MinConf { get; set; }

//    [JsonProperty("depositAddress")]
//    public string DepositAddress { get; set; }

//    [JsonProperty("blockchain")]
//    public string Blockchain { get; set; }

//    [JsonProperty("delisted")]
//    public bool Delisted { get; set; }

//    [JsonProperty("tradingState")]
//    public string TradingState { get; set; }

//    [JsonProperty("walletState")]
//    public string WalletState { get; set; }

//    [JsonProperty("walletDepositState")]
//    public string WalletDepositState { get; set; }

//    [JsonProperty("walletWithdrawalState")]
//    public string WalletWithdrawalState { get; set; }

//    [JsonProperty("supportCollateral")]
//    public bool SupportCollateral { get; set; }

//    [JsonProperty("supportBorrow")]
//    public bool SupportBorrow { get; set; }
//}



