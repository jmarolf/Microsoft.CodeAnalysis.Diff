using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.CodeAnalysis.Diff
{
    public static class SyntaxTreeMerger
    {
        /// <summary>
        /// The algorithm takes three trees as an input:
        ///     the original tree TO, “my” tree TA, and “their” tree TB.
        /// From a highlevel perspective, the algorithm works as follows:
        ///   - We ﬁrst decide which nodes to keep seeing our trees as a set of nodes,
        ///     we take nodes added by A and B,as well as the original nodes that
        ///     both A and B agreed to keep
        ///   - Then, we “re-wire” the nodes by attaching them to their parents;
        ///     we follow the “parenting” choices from A; for nodes that are still not 
        ///     reattached after that, we follow the choices from B.
        ///     This operation may create cycles; we provide a cycle-breaking procedure. 
        ///   - Once each node is attached to the right parent, we decide on an order for the siblings;
        ///     that is, we give a total order for the transversal dotted arrows.
        ///     We use a diff3-like algorithm on the sequence of siblings.
        ///   - Finally, we merge properties of the node, such as the actual name
        ///     (rather than the internal id) of a variable binding
        /// The merge algorithm we describe enjoys the following properties:
        ///   - If both sides leave a piece of code untouched; it remains untouched in the output tree. 
        ///   - If one of the two sides changes a piece of code,the output tree contains the change.
        ///   - If both sides agree on a change, then the output tree contains an identical change.
        ///   - If both sides argue about a change, then A wins.
        /// </summary>
        /// <param name="base">The original tree</param>
        /// <param name="left">“my” tree</param>
        /// <param name="right">“their” tree</param>
        /// <returns></returns>
        public static SyntaxTree MergeTrees(SyntaxTree @base,
                                            SyntaxTree mine,
                                            SyntaxTree theirs,
                                            Resolution resolution = Resolution.Mine)
        {
            if (@base is null)
                throw new ArgumentNullException(nameof(@base));
            if (mine is null)
                throw new ArgumentNullException(nameof(mine));
            if (theirs is null)
                throw new ArgumentNullException(nameof(theirs));

            return MergeTreesInternal(@base, mine, theirs, resolution);
        }

        public static bool TryMergeTrees(SyntaxTree @base,
                                         SyntaxTree mine,
                                         SyntaxTree theirs,
                                         out SyntaxTree mergedSyntaxTree)
        {
            if (@base is null)
                throw new ArgumentNullException(nameof(@base));
            if (mine is null)
                throw new ArgumentNullException(nameof(mine));
            if (theirs is null)
                throw new ArgumentNullException(nameof(theirs));

            mergedSyntaxTree = default;
            try
            {
                mergedSyntaxTree = MergeTreesInternal(@base, mine, theirs);
                return true;
            }
            catch (MergeFailedException)
            {
                return false;
            }
        }

        private static SyntaxTree MergeTreesInternal(SyntaxTree @base,
                                                     SyntaxTree mine,
                                                     SyntaxTree theirs,
                                                     Resolution? resolution = null)
        {
            // We ﬁrst decide which nodes to keep seeing our trees as a set of nodes,
            // we take nodes added by A and B,as well as the original nodes that
            // both A and B agreed to keep
            var leftDiff = SyntaxTreeDiffer.ComputeDiff(@base, mine);
            var rightDiff = SyntaxTreeDiffer.ComputeDiff(@base, theirs);

            // Then, we “re-wire” the nodes by attaching them to their parents;
            // we follow the “parenting” choices from A; for nodes that are still not 
            // reattached after that, we follow the choices from B.
            // This operation may create cycles; we provide a cycle-breaking procedure.

            // Once each node is attached to the right parent, we decide on an order for the siblings;
            // that is, we give a total order for the transversal dotted arrows.
            // We use a diff3-like algorithm on the sequence of siblings.

            return null;
        }
    }
}
