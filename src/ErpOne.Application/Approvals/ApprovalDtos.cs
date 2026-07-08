namespace ErpOne.Application.Approvals;

public record ApprovalStepDto(
    int Id, int StepOrder, string RoleName, string Status,
    string? ActedByName, DateTime? ActedAt, string? Note);

public record ApprovalChainStepDto(int Id, int StepOrder, string RoleName);

public record ApprovalChainStepInput(int StepOrder, string RoleName);
