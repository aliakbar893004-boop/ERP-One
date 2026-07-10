namespace ErpOne.Application.Numbering;

public record NumberSequenceDto(int Id, string Code, string Prefix, string DateFormat, int Padding, string ResetPeriod, string Separator, string Sample);
public record UpdateNumberSequenceRequest(string Prefix, string DateFormat, int Padding, string ResetPeriod, string Separator);
