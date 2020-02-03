using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.CodeAnalysis.Diff
{
    public static partial class SyntaxTreeDiffer
    {
        private class SyntaxDiffer
        {
            private readonly Stack<SyntaxNodeOrToken> _oldNodes = new Stack<SyntaxNodeOrToken>(8);
            private readonly Stack<SyntaxNodeOrToken> _newNodes = new Stack<SyntaxNodeOrToken>(8);
            private readonly HashSet<GreenNode> _nodeSimilaritySet = new HashSet<GreenNode>();
            private readonly HashSet<string> _tokenTextSimilaritySet = new HashSet<string>();
            private const int MaxSearchLength = 8;
            private readonly List<TreeChange> _changes = new List<TreeChange>();

            public SyntaxDiffer(SyntaxNode oldNode, SyntaxNode newNode)
            {
                _oldNodes.Push(oldNode);
                _newNodes.Push(newNode);
            }

            public void ComputeChanges()
            {
                while (true)
                {
                    // first check end-of-lists termination cases...
                    if (_newNodes.Count == 0)
                    {
                        // remaining old nodes are deleted
                        if (_oldNodes.Count > 0)
                        {
                            DeleteOld(_oldNodes.Count);
                        }
                        break;
                    }
                    else if (_oldNodes.Count == 0)
                    {
                        // remaining nodes were inserted
                        if (_newNodes.Count > 0)
                        {
                            InsertNew(_newNodes.Count);
                        }
                        break;
                    }
                    else
                    {
                        var action = GetNextAction();
                        switch (action.Operation)
                        {
                            case DiffOperation.SkipBoth:
                                RemoveFirst(_oldNodes, action.Count);
                                RemoveFirst(_newNodes, action.Count);
                                break;
                            case DiffOperation.ReduceOld:
                                ReplaceFirstWithChildren(_oldNodes);
                                break;
                            case DiffOperation.ReduceNew:
                                ReplaceFirstWithChildren(_newNodes);
                                break;
                            case DiffOperation.ReduceBoth:
                                ReplaceFirstWithChildren(_oldNodes);
                                ReplaceFirstWithChildren(_newNodes);
                                break;
                            case DiffOperation.InsertNew:
                                InsertNew(action.Count);
                                break;
                            case DiffOperation.DeleteOld:
                                DeleteOld(action.Count);
                                break;
                            case DiffOperation.ReplaceOldWithNew:
                                ReplaceOldWithNew(action.Count, action.Count);
                                break;
                        }
                    }
                }
            }

            private List<TreeChange> ReduceChanges(List<TreeChange> treeChanges)
            {
                var textChanges = new List<ChangeRangeWithText>(changeRecords.Count);

                var oldText = new StringBuilder();
                var newText = new StringBuilder();

                foreach (var cr in changeRecords)
                {
                    // try to reduce change range by finding common characters
                    if (cr.Range.Span.Length > 0 && cr.Range.NewLength > 0)
                    {
                        var range = cr.Range;

                        CopyText(cr.OldNodes, oldText);
                        CopyText(cr.NewNodes, newText);

                        int commonLeadingCount;
                        int commonTrailingCount;
                        GetCommonEdgeLengths(oldText, newText, out commonLeadingCount, out commonTrailingCount);

                        // did we have any common leading or trailing characters between the strings?
                        if (commonLeadingCount > 0 || commonTrailingCount > 0)
                        {
                            range = new TextChangeRange(
                                new TextSpan(range.Span.Start + commonLeadingCount, range.Span.Length - (commonLeadingCount + commonTrailingCount)),
                                range.NewLength - (commonLeadingCount + commonTrailingCount));

                            if (commonTrailingCount > 0)
                            {
                                newText.Remove(newText.Length - commonTrailingCount, commonTrailingCount);
                            }

                            if (commonLeadingCount > 0)
                            {
                                newText.Remove(0, commonLeadingCount);
                            }
                        }

                        // only include adjusted change if there is still a change 
                        if (range.Span.Length > 0 || range.NewLength > 0)
                        {
                            textChanges.Add(new ChangeRangeWithText(range, _computeNewText ? newText.ToString() : null));
                        }
                    }
                    else
                    {
                        // pure inserts and deletes
                        textChanges.Add(new ChangeRangeWithText(cr.Range, _computeNewText ? GetText(cr.NewNodes) : null));
                    }
                }

                return textChanges;
            }

            private DiffAction GetNextAction()
            {
                bool oldIsToken = _oldNodes.Peek().IsToken;
                bool newIsToken = _newNodes.Peek().IsToken;

                // look for exact match
                int indexOfOldInNew;
                int similarityOfOldInNew;
                int indexOfNewInOld;
                int similarityOfNewInOld;

                FindBestMatch(_newNodes, _oldNodes.Peek(), out indexOfOldInNew, out similarityOfOldInNew);
                FindBestMatch(_oldNodes, _newNodes.Peek(), out indexOfNewInOld, out similarityOfNewInOld);

                if (indexOfOldInNew == 0 && indexOfNewInOld == 0)
                {
                    // both first nodes are somewhat similar to each other

                    if (AreIdentical(_oldNodes.Peek(), _newNodes.Peek()))
                    {
                        // they are identical, so just skip over both first new and old nodes.
                        return new DiffAction(DiffOperation.SkipBoth, 1);
                    }
                    else if (!oldIsToken && !newIsToken)
                    {
                        // neither are tokens, so replace each first node with its child nodes
                        return new DiffAction(DiffOperation.ReduceBoth, 1);
                    }
                    else
                    {
                        // otherwise just claim one's text replaces the other.. 
                        // NOTE: possibly we can improve this by reducing the side that may not be token?
                        return new DiffAction(DiffOperation.ReplaceOldWithNew, 1);
                    }
                }
                else if (indexOfOldInNew >= 0 || indexOfNewInOld >= 0)
                {
                    // either the first old-node is similar to some node in the new-list or
                    // the first new-node is similar to some node in the old-list

                    if (indexOfNewInOld < 0 || similarityOfOldInNew >= similarityOfNewInOld)
                    {
                        // either there is no match for the first new-node in the old-list or the 
                        // the similarity of the first old-node in the new-list is much greater

                        // if we find a match for the old node in the new list, that probably means nodes were inserted before it.
                        if (indexOfOldInNew > 0)
                        {
                            // look ahead to see if the old node also appears again later in its own list
                            int indexOfOldInOld;
                            int similarityOfOldInOld;
                            FindBestMatch(_oldNodes, _oldNodes.Peek(), out indexOfOldInOld, out similarityOfOldInOld, 1);

                            // don't declare an insert if the node also appeared later in the original list
                            var oldHasSimilarSibling = (indexOfOldInOld >= 1 && similarityOfOldInOld >= similarityOfOldInNew);
                            if (!oldHasSimilarSibling)
                            {
                                return new DiffAction(DiffOperation.InsertNew, indexOfOldInNew);
                            }
                        }

                        if (!newIsToken)
                        {
                            if (AreSimilar(_oldNodes.Peek(), _newNodes.Peek()))
                            {
                                return new DiffAction(DiffOperation.ReduceBoth, 1);
                            }
                            else
                            {
                                return new DiffAction(DiffOperation.ReduceNew, 1);
                            }
                        }
                        else
                        {
                            return new DiffAction(DiffOperation.ReplaceOldWithNew, 1);
                        }
                    }
                    else
                    {
                        if (indexOfNewInOld > 0)
                        {
                            return new DiffAction(DiffOperation.DeleteOld, indexOfNewInOld);
                        }
                        else if (!oldIsToken)
                        {
                            if (AreSimilar(_oldNodes.Peek(), _newNodes.Peek()))
                            {
                                return new DiffAction(DiffOperation.ReduceBoth, 1);
                            }
                            else
                            {
                                return new DiffAction(DiffOperation.ReduceOld, 1);
                            }
                        }
                        else
                        {
                            return new DiffAction(DiffOperation.ReplaceOldWithNew, 1);
                        }
                    }
                }
                else
                {
                    // no similarities between first node of old-list in new-list or between first new-node in old-list
                    if (!oldIsToken && !newIsToken)
                    {
                        // check similarity anyway
                        var sim = GetSimilarity(_oldNodes.Peek(), _newNodes.Peek());
                        if (sim >= Math.Max(_oldNodes.Peek().FullSpan.Length, _newNodes.Peek().FullSpan.Length))
                        {
                            return new DiffAction(DiffOperation.ReduceBoth, 1);
                        }
                    }

                    return new DiffAction(DiffOperation.ReplaceOldWithNew, 1);
                }
            }

            private struct DiffAction
            {
                public readonly DiffOperation Operation;
                public readonly int Count;

                public DiffAction(DiffOperation operation, int count)
                {
                    System.Diagnostics.Debug.Assert(count >= 0);
                    this.Operation = operation;
                    this.Count = count;
                }
            }

            private enum DiffOperation
            {
                None = 0,
                SkipBoth,
                ReduceOld,
                ReduceNew,
                ReduceBoth,
                InsertNew,
                DeleteOld,
                ReplaceOldWithNew
            }

            private static void RemoveFirst(Stack<SyntaxNodeOrToken> stack, int count)
            {
                for (int i = 0; i < count; ++i)
                {
                    stack.Pop();
                }
            }

            private static void ReplaceFirstWithChildren(Stack<SyntaxNodeOrToken> stack)
            {
                var node = stack.Pop();

                int c = 0;
                var children = new SyntaxNodeOrToken[node.ChildNodesAndTokens().Count];
                foreach (var child in node.ChildNodesAndTokens())
                {
                    if (child.FullSpan.Length > 0)
                    {
                        children[c] = child;
                        c++;
                    }
                }

                for (int i = c - 1; i >= 0; i--)
                {
                    stack.Push(children[i]);
                }
            }

            private void InsertNew(int newNodeCount)
            {
                var newSpan = GetSpan(_newNodes, 0, newNodeCount);
                var insertedNodes = CopyFirst(_newNodes, newNodeCount);
                RemoveFirst(_newNodes, newNodeCount);
                int start = _oldNodes.Count > 0 ? _oldNodes.Peek().Position : _oldSpan.End;
                RecordChange(new ChangeRecord(new TextChangeRange(new TextSpan(start, 0), newSpan.Length), null, insertedNodes));
            }

            private void DeleteOld(int oldNodeCount)
            {
                var oldSpan = GetSpan(_oldNodes, 0, oldNodeCount);
                var removedNodes = CopyFirst(_oldNodes, oldNodeCount);
                RemoveFirst(_oldNodes, oldNodeCount);
                RecordChange(new ChangeRecord(new TextChangeRange(oldSpan, 0), removedNodes, null));
            }

            private void ReplaceOldWithNew(int oldNodeCount, int newNodeCount)
            {
                if (oldNodeCount == 1 && newNodeCount == 1)
                {
                    // Avoid creating a Queue<T> which we immediately discard in the most common case for old/new counts
                    var removedNode = _oldNodes.Pop();
                    var oldSpan = removedNode.FullSpan;

                    var insertedNode = _newNodes.Pop();
                    var newSpan = insertedNode.FullSpan;

                    RecordChange(new TextChangeRange(oldSpan, newSpan.Length), removedNode, insertedNode);
                }
                else
                {
                    var oldSpan = GetSpan(_oldNodes, 0, oldNodeCount);
                    var removedNodes = CopyFirst(_oldNodes, oldNodeCount);
                    RemoveFirst(_oldNodes, oldNodeCount);
                    var newSpan = GetSpan(_newNodes, 0, newNodeCount);
                    var insertedNodes = CopyFirst(_newNodes, newNodeCount);
                    RemoveFirst(_newNodes, newNodeCount);
                    RecordChange(new ChangeRecord(new TextChangeRange(oldSpan, newSpan.Length), removedNodes, insertedNodes));
                }
            }

            private void FindBestMatch(Stack<SyntaxNodeOrToken> stack, in SyntaxNodeOrToken node, out int index, out int similarity, int startIndex = 0)
            {
                index = -1;
                similarity = -1;

                int i = 0;
                foreach (var stackNode in stack)
                {
                    if (i >= MaxSearchLength)
                    {
                        break;
                    }

                    if (i >= startIndex)
                    {
                        if (AreIdentical(stackNode, node))
                        {
                            var sim = node.FullSpan.Length;
                            if (sim > similarity)
                            {
                                index = i;
                                similarity = sim;
                                return;
                            }
                        }
                        else if (AreSimilar(stackNode, node))
                        {
                            var sim = GetSimilarity(stackNode, node);

                            // Are these really the same? This may be expensive so only check this if 
                            // similarity is rated equal to them being identical.
                            if (sim == node.FullSpan.Length && node.IsToken)
                            {
                                if (stackNode.ToFullString() == node.ToFullString())
                                {
                                    index = i;
                                    similarity = sim;
                                    return;
                                }
                            }

                            if (sim > similarity)
                            {
                                index = i;
                                similarity = sim;
                            }
                        }
                        else
                        {
                            // check one level deep inside list node's children
                            int j = 0;
                            foreach (var child in stackNode.ChildNodesAndTokens())
                            {
                                if (j >= MaxSearchLength)
                                {
                                    break;
                                }

                                j++;

                                if (AreIdentical(child, node))
                                {
                                    index = i;
                                    similarity = node.FullSpan.Length;
                                    return;
                                }
                                else if (AreSimilar(child, node))
                                {
                                    var sim = GetSimilarity(child, node);
                                    if (sim > similarity)
                                    {
                                        index = i;
                                        similarity = sim;
                                    }
                                }
                            }
                        }
                    }

                    i++;
                }
            }

            private int GetSimilarity(in SyntaxNodeOrToken node1, in SyntaxNodeOrToken node2)
            {
                // count the characters in the common/identical nodes
                int w = 0;
                _nodeSimilaritySet.Clear();
                _tokenTextSimilaritySet.Clear();

                if (node1.IsToken && node2.IsToken)
                {
                    var text1 = node1.ToString();
                    var text2 = node2.ToString();

                    if (text1 == text2)
                    {
                        // main text of token is the same
                        w += text1.Length;
                    }

                    foreach (var tr in node1.GetLeadingTrivia())
                    {
                        _nodeSimilaritySet.Add(tr.UnderlyingNode);
                    }

                    foreach (var tr in node1.GetTrailingTrivia())
                    {
                        _nodeSimilaritySet.Add(tr.UnderlyingNode);
                    }

                    foreach (var tr in node2.GetLeadingTrivia())
                    {
                        if (_nodeSimilaritySet.Contains(tr.UnderlyingNode))
                        {
                            w += tr.FullSpan.Length;
                        }
                    }

                    foreach (var tr in node2.GetTrailingTrivia())
                    {
                        if (_nodeSimilaritySet.Contains(tr.UnderlyingNode))
                        {
                            w += tr.FullSpan.Length;
                        }
                    }
                }
                else
                {
                    foreach (var n1 in node1.ChildNodesAndTokens())
                    {
                        _nodeSimilaritySet.Add(n1.UnderlyingNode);

                        if (n1.IsToken)
                        {
                            _tokenTextSimilaritySet.Add(n1.ToString());
                        }
                    }

                    foreach (var n2 in node2.ChildNodesAndTokens())
                    {
                        if (_nodeSimilaritySet.Contains(n2.UnderlyingNode))
                        {
                            w += n2.FullSpan.Length;
                        }
                        else if (n2.IsToken)
                        {
                            var tokenText = n2.ToString();
                            if (_tokenTextSimilaritySet.Contains(tokenText))
                            {
                                w += tokenText.Length;
                            }
                        }
                    }
                }

                return w;
            }

            private static bool AreIdentical(in SyntaxNodeOrToken node1, in SyntaxNodeOrToken node2)
            {
                return node1.UnderlyingNode == node2.UnderlyingNode;
            }

            private static bool AreSimilar(in SyntaxNodeOrToken node1, in SyntaxNodeOrToken node2)
            {
                return node1.RawKind == node2.RawKind;
            }

        }
    }


}
