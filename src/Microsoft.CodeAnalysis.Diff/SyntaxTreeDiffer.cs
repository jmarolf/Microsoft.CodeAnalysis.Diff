using System;
using System.Collections.Immutable;
using System.Text;

namespace Microsoft.CodeAnalysis.Diff
{
    public static partial class SyntaxTreeDiffer
    {
        public static ImmutableArray<TreeChange> ComputeDiff(SyntaxTree before, SyntaxTree after)
        {
            if (before == after)
            {
                return ImmutableArray<TreeChange>.Empty;
            }
            else if (before is null)
            {
                return ImmutableArray.Create<TreeChange>(TreeChange.FromTree(after));
            }
            else if (after is null)
            {
                throw new ArgumentNullException(nameof(after));
            }
            else
            {
                return ComputeDiff(before.GetRoot(), after.GetRoot());
            }
        }

        private static ImmutableArray<TreeChange> ComputeDiff(SyntaxNode before, SyntaxNode after)
        {
            var differ = new SyntaxDiffer(before, after);
        }
    }


}
