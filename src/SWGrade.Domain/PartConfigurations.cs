using System;
using System.Collections.Generic;
using System.Linq;

namespace SWGrade.Domain
{
    public sealed class PartConfigurations
    {
        public PartConfigurations(string documentHash, string activeName, IEnumerable<string> names)
        {
            DocumentHash = documentHash;
            Names = Array.AsReadOnly(names.Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal).ToArray());
            ActiveName = Contains(activeName) ? activeName : null;
        }

        public string DocumentHash { get; }
        public string ActiveName { get; }
        public IReadOnlyList<string> Names { get; }

        public bool Contains(string name) => Names.Contains(name, StringComparer.Ordinal);
    }
}
