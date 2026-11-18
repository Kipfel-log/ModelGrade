using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SWGrade.SolidWorks
{
    // Resolve members on the installed COM interfaces. A ModelDoc2 RCW also
    // supports IPartDoc, whose members need not be on its default IDispatch.
    internal sealed class CadInterop : IDisposable
    {
        internal const string Core = "SolidWorks.Interop.sldworks";
        internal const string Constants = "SolidWorks.Interop.swconst";
        internal const string Utilities = "SolidWorks.Interop.gtswutilities";

        private readonly Dictionary<string, Assembly> assemblies = new Dictionary<string, Assembly>();
        private readonly HashSet<object> references = new HashSet<object>();
        private readonly List<object> releaseOrder = new List<object>();

        private readonly Action<string> report;

        internal CadInterop(Action<string> report)
        {
            this.report = report;
        }

        internal void Log(string message)
        {
            report(message);
        }

        internal void Log(string format, params object[] args)
        {
            report(string.Format(CultureInfo.InvariantCulture, format, args));
        }

        internal void Load(string folder, string name)
        {
            string path = Path.Combine(folder, name + ".dll");
            if (!File.Exists(path))
                throw new FileNotFoundException("安装目录中缺少 Interop：" + path);

            Assembly assembly = Assembly.LoadFrom(path);
            if (!string.Equals(Path.GetFullPath(assembly.Location), Path.GetFullPath(path),
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("当前进程加载了另一安装目录的 Interop。请重新启动 SWGrade 后连接目标 CAD。");

            Log("Interop：{0}", assembly.GetName().FullName);
            assemblies.Add(name, assembly);
        }

        internal Type Interface(string name, string library = Core)
        {
            return assemblies[library].GetType(library + "." + name, true);
        }

        internal int Constant(string type, string name, string library = Constants)
        {
            return Convert.ToInt32(Enum.Parse(Interface(type, library), name));
        }

        internal string EnumName(string type, int value)
        {
            return Enum.GetName(Interface(type, Constants), value) ?? "未识别";
        }

        internal string Flags(string type, int value)
        {
            return Enum.Format(Interface(type, Constants), value, "F");
        }

        internal object Call(string type, object target, string name, params object[] args)
        {
            return Invoke(Interface(type).GetMethod(name), target, args);
        }

        internal object Get(string type, object target, string name)
        {
            return Invoke(Interface(type).GetProperty(name)?.GetGetMethod(), target, null);
        }

        internal void Set(string type, object target, string name, object value)
        {
            Invoke(Interface(type).GetProperty(name)?.GetSetMethod(), target, new[] { value });
        }

        internal object Invoke(MethodInfo member, object target, object[] args)
        {
            if (member == null)
                throw new MissingMemberException("本机 Interop 缺少请求的 API 成员，请核对版本。");
            if (target == null || !member.DeclaringType.IsInstanceOfType(target))
                throw new InvalidCastException("SOLIDWORKS 未提供接口 " + member.DeclaringType.FullName);

            object result = member.Invoke(target, args);
            if (result is Array array)
            {
                if (!array.GetType().GetElementType().IsValueType)
                {
                    foreach (object item in array)
                        Track(item);
                }
            }
            else
            {
                Track(result);
            }

            return result;
        }

        internal void Track(object value)
        {
            if (value != null && Marshal.IsComObject(value) && references.Add(value))
                releaseOrder.Add(value);
        }

        public void Dispose()
        {
            // Release each distinct RCW once. Never force final release.
            for (int index = releaseOrder.Count - 1; index >= 0; index--)
                Marshal.ReleaseComObject(releaseOrder[index]);
        }
    }
}
