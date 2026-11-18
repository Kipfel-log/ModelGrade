using System;
using System.Collections.Generic;
using System.Linq;

namespace SWGrade.SolidWorks
{
    internal static class OpenCadParts
    {
        private const string App = "ISldWorks";
        private const string Model = "IModelDoc2";
        private const string Part = "IPartDoc";

        internal static IReadOnlyList<OpenPartInfo> List(CadInterop cad, CadSession session)
        {
            // https://help.solidworks.com/2025/english/api/sldworksapi/get_paths_of_open_documents_example_vb.htm
            var documents = (Array)cad.Call(App, session.App, "GetDocuments");
            var parts = new List<OpenPartInfo>();
            if (documents == null)
                return parts;

            foreach (var document in documents)
            {
                if (document != null && cad.Interface(Part).IsInstanceOfType(document) &&
                    (bool)cad.Get(Model, document, "Visible"))
                    parts.Add(Describe(cad, session, document));
            }

            return parts.OrderBy(part => part.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(part => part.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal static OpenPartInfo Active(CadInterop cad, CadSession session)
        {
            // ActiveDoc identifies the user's source. Measurement still uses OpenDoc6's returned document.
            // https://help.solidworks.com/2025/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ISldWorks~ActiveDoc.html
            var document = cad.Get(App, session.App, "ActiveDoc");
            if (document == null)
                throw new InvalidOperationException("SOLIDWORKS 没有当前活动文档。请先打开一个零件。");
            if (!cad.Interface(Part).IsInstanceOfType(document))
                throw new InvalidOperationException("当前活动文档不是零件。请先在 SOLIDWORKS 切换到一个零件。");
            return Describe(cad, session, document);
        }

        internal static OpenPartInfo RefreshSelection(CadInterop cad, CadSession session, OpenPartInfo selected)
        {
            if (selected.ProcessId != session.ProcessId)
                throw new InvalidOperationException("SOLIDWORKS 实例已变化，请重新选择零件。");

            var document = cad.Call(App, session.App, "GetOpenDocumentByName", selected.Path);
            if (document == null || !cad.Interface(Part).IsInstanceOfType(document) ||
                !(bool)cad.Get(Model, document, "Visible") ||
                !string.Equals((string)cad.Call(Model, document, "GetPathName"), selected.Path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("所选零件已关闭或路径已变化，请重新选择。");

            return Describe(cad, session, document);
        }

        private static OpenPartInfo Describe(CadInterop cad, CadSession session, object document)
        {
            var title = (string)cad.Call(Model, document, "GetTitle");
            var path = (string)cad.Call(Model, document, "GetPathName");
            var manager = cad.Get(Model, document, "ConfigurationManager");
            var configuration = manager == null ? null : cad.Get("IConfigurationManager", manager, "ActiveConfiguration");
            var name = configuration == null ? null : (string)cad.Get("IConfiguration", configuration, "Name");
            // GetSaveFlag can also be set for a document from an earlier SOLIDWORKS version.
            // https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelDoc2~GetSaveFlag.html
            var needsSave = (bool)cad.Call(Model, document, "GetSaveFlag");
            return new OpenPartInfo(session.ProcessId, title, path, name, needsSave);
        }
    }
}
