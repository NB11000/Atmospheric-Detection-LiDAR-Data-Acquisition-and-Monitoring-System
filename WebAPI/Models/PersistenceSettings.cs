namespace WebAPI.Models
{
    /// <summary>
    /// 持久化配置选项
    /// 通过 appsettings.json 中 "Persistence" 节点绑定，支持 ASP.NET Core Options 模式
    /// </summary>
    public class PersistenceSettings
    {
        /// <summary>
        /// CSV 数据文件输出目录（支持绝对路径或相对于工作目录的路径）
        /// </summary>
        public string DataDirectory { get; set; } = "data";
    }
}
