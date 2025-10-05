using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UTools.SourceGenerators
{
    public class UsingDirectiveSyntaxComparerWithAlias : IEqualityComparer<UsingDirectiveSyntax>
    {
        public bool Equals(UsingDirectiveSyntax x, UsingDirectiveSyntax y)
        {
            // Check if both are null or the same instance
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            // Check if both have aliases and compare them, or if neither has an alias, compare their names
            bool aliasesEqual = (x.Alias == null && y.Alias == null) ||
                                (x.Alias != null && y.Alias != null && x.Alias.Name.ToString() == y.Alias.Name.ToString());
            bool namesEqual = x.Name.ToString() == y.Name.ToString();

            return aliasesEqual && namesEqual;
        }

        public int GetHashCode(UsingDirectiveSyntax obj)
        {
            int hashAlias = obj.Alias?.Name.ToString().GetHashCode() ?? 0;
            int hashName = obj.Name.ToString().GetHashCode();
            return hashAlias ^ hashName;
        }
    }

    public static class NamespaceHelpers
    {
        private static UsingDirectiveSyntaxComparerWithAlias m_UsingComparer = new();

        public static IEnumerable<UsingDirectiveSyntax> MakeDistinct(this IEnumerable<UsingDirectiveSyntax> usingDirectives)
        {
            return usingDirectives.Distinct(m_UsingComparer);
        }
    }
}