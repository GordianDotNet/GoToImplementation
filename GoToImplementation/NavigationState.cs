using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using Microsoft.VisualStudio.Text;

namespace GoToImplementation
{
    public class NavigationState
    {
        public List<ISymbol> SymbolsToVisit;
        public SnapshotPoint NavigateToPositionAtTheEnd;
    }
}
