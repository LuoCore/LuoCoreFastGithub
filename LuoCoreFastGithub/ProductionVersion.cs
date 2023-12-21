using System;
using System.Reflection;
using System.Text.RegularExpressions;

namespace LuoCoreFastGithub
{
    /// <summary>
    /// 表示产品版本
    /// </summary>
    public class ProductionVersion : IComparable<ProductionVersion>
    {
        private static string? productionVersion;


        /// <summary>
        /// 获取当前应用程序的产品版本
        /// </summary>
        public static ProductionVersion? Current
        {
            get
            {
                if (productionVersion == null)
                {
                    productionVersion = Assembly
                        .GetEntryAssembly()?
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                        .InformationalVersion;
                }
                return productionVersion == null ? null : Parse(productionVersion);
            }
        }
        /// <summary>
        /// 版本
        /// </summary>
        public Version Version { get; }

        /// <summary>
        /// 子版本
        /// </summary>
        public string SubVersion { get; }

        /// <summary>
        /// 产品版本
        /// </summary>
        /// <param name="version"></param>
        /// <param name="subVersion"></param>
        public ProductionVersion(Version version, string subVersion)
        {
            this.Version = version;
            this.SubVersion = subVersion;
        }

        /// <summary>
        /// 比较版本
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public int CompareTo(ProductionVersion? other)
        {
            var x = this;
            var y = other;

            if (y == null)
            {
                return 1;
            }

            var value = x.Version.CompareTo(y.Version);
            if (value == 0)
            {
                value = CompareSubVerson(x.SubVersion, y.SubVersion);
            }
            return value;

            static int CompareSubVerson(string subX, string subY)
            {
                if (subX.Length == 0 && subY.Length == 0)
                {
                    return 0;
                }
                if (subX.Length == 0)
                {
                    return 1;
                }
                if (subY.Length == 0)
                {
                    return -1;
                }

                return StringComparer.OrdinalIgnoreCase.Compare(subX, subY);
            }
        }

        public override string ToString()
        {
            return $"{Version}{SubVersion}";
        }

        /// <summary>
        /// 解析
        /// </summary>
        /// <param name="productionVersion"></param>
        /// <returns></returns>
        public static ProductionVersion Parse(string productionVersion)
        {
            if (string.IsNullOrEmpty(productionVersion))
            {
                throw new ArgumentException("productionVersion cannot be null or empty");
            }

            const string VERSION = @"^\d+\.\d+\.\d+";  // 匹配 X.X.X 格式的版本号
            var match = Regex.Match(productionVersion, VERSION);
            var verion = match.Success ? match.Value : "";


            if (string.IsNullOrEmpty(verion))
            {
                throw new FormatException("Invalid productionVersion format");
            }

            var subVersion = productionVersion[verion.Length..];
            return new ProductionVersion(Version.Parse(verion), subVersion);
        }
    }
}
