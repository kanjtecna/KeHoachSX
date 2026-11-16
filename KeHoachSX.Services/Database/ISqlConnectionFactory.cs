using System.Data;
namespace KeHoachSX.Infrastructure.Database;
public interface ISqlConnectionFactory { IDbConnection CreateConnection(); }
