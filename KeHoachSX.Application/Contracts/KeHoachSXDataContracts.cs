namespace KeHoachSX.Application.Contracts;

public sealed record SkillMatrixRecord(string WorkerId, string FullName, string ProductCode, int Count, bool IncludeInSkillMap = true);

public sealed record AssemblyEmployeeRecord(
    string WorkerId,
    string FullName,
    string GroupName,
    string LevelUser,
    string Status,
    double Efficiency);

public sealed record OperatorContext(string EmployeeCode, string EmployeeName, string MachineName);