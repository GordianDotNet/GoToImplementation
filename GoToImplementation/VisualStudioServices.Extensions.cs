using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.Linq;
using Microsoft.CodeAnalysis.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Threading;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using System.Diagnostics;
using System;

namespace GoToImplementation
{
    public partial class VisualStudioServices
    {
        public void ShowMessageBox(string message, string title = "GoToImplementation Extension")
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(
                this.Package,
                message,
                title,
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        public async Task<bool> GoToImplementationAsync()
        {
            var wpfTextView = await GetWpfTextViewAsync();
            var currentPosition = wpfTextView.Caret.Position.BufferPosition;

            var document = currentPosition.Snapshot.GetOpenDocumentInCurrentContextWithChanges();
            if (document == null)
            {
                // no document found => we can't help
                return false;
            }

            var semanticModel = await document.GetSemanticModelAsync();

            // find symbol at caret position
            var selectedSymbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, currentPosition.Position, document.Project.Solution.Workspace);
            if (selectedSymbol == null)
            {
                // no symbol selected => we can't help
                return false;
            }

            if (await NavigateThroughInterfaceImplementationsAsync(document, selectedSymbol, currentPosition))
            {
                return true;
            }

            _interfaceNavigationState = null;

            // search implementation only if method or property symbol
            if (selectedSymbol is IMethodSymbol || selectedSymbol is IPropertySymbol)
            {
                try
                {
                    // prio 1: we're assuming it's an interface method/property => find implementations
                    var implSymbols = await SymbolFinder.FindImplementationsAsync(selectedSymbol, document.Project.Solution);
                    if (implSymbols.Any())
                    {
                        var symbolsToVisit = new List<ISymbol>();
                        symbolsToVisit.Add(selectedSymbol);
                        symbolsToVisit.AddRange(implSymbols);
                        _interfaceNavigationState = new NavigationState
                        {
                            SymbolsToVisit = symbolsToVisit,
                            NavigateToPositionAtTheEnd = currentPosition
                        };
                        return await TryGoToDefinitionOrNavigateBackwardAsync(document, selectedSymbol, currentPosition);
                    }

                    // prio 2: we assume that it is an implementation of an interface method/property
                    // we will jump betweem each implementation
                    var interfaceSymbols = await SymbolFinder.FindImplementedInterfaceMembersAsync(selectedSymbol, document.Project.Solution);
                    if (interfaceSymbols.Count() > 0)
                    {
                        var implInterfaceSymbols = new List<ISymbol>();
                        foreach (var interfaceSymbol in interfaceSymbols)
                        {
                            implInterfaceSymbols.AddRange(await SymbolFinder.FindImplementationsAsync(interfaceSymbol, document.Project.Solution));
                        }

                        if (implInterfaceSymbols.Any())
                        {
                            NavigateTo(implInterfaceSymbols, document.Project, selectedSymbol);
                            return true;
                        }
                    }

                    // prio 3: we assume override or abstract
                    var overrideSymbols = await SymbolFinder.FindOverridesAsync(selectedSymbol, document.Project.Solution);
                    if (overrideSymbols.Any())
                    {
                        NavigateTo(overrideSymbols, document.Project, selectedSymbol);
                        return true;
                    }
                }
                catch (System.Exception ex)
                {
                    ShowMessageBox($"{ex.Message}:\n{ex.StackTrace}");
                }
            }

            // default behaviour: Go to definition as F12 or NavigateBackward Ctrl + -
            return await TryGoToDefinitionOrNavigateBackwardAsync(document, selectedSymbol, currentPosition);
        }

        

        private NavigationState _interfaceNavigationState;
        private async Task<bool> NavigateThroughInterfaceImplementationsAsync(Document currentDocument, ISymbol selectedSymbol, SnapshotPoint currentPosition)
        {
            if (_interfaceNavigationState != null)
            {
                // check if count is greater than 2
                if (_interfaceNavigationState.SymbolsToVisit.Count() > 1)
                {
                    // check if user didn't move elsewhere
                    if (selectedSymbol.ToString() == _interfaceNavigationState.SymbolsToVisit.First().ToString())
                    {
                        _interfaceNavigationState.SymbolsToVisit.RemoveAt(0);
                        return TryGoToDefinition(currentDocument.Project, _interfaceNavigationState.SymbolsToVisit.First());
                    }
                    else
                    {
                        _interfaceNavigationState = null;
                    }
                }
                else if (_interfaceNavigationState.SymbolsToVisit.Count() == 1)
                {
                    if (selectedSymbol.ToString() == _interfaceNavigationState.SymbolsToVisit.First().ToString())
                    {
                        return await GotoPositionAsync(currentDocument, _interfaceNavigationState.NavigateToPositionAtTheEnd);
                    }
                }
            }

            return false;
        }

        private async Task<bool> GotoPositionAsync(Document currentDocument, SnapshotPoint position)
        {
            try
            {
                var newDocument = position.Snapshot.GetOpenDocumentInCurrentContextWithChanges();
                if (newDocument == null)
                {
                    // document was closed => preview window
                    await NavigateBackAsync();
                    return true;
                }

                if (currentDocument.Id != newDocument.Id)
                {
                    VisualStudioWorkspace.OpenDocument(newDocument.Id, true);
                    //await System.Threading.Tasks.Task.Delay(1000);
                }

                //VisualStudioWorkspace.OpenDocument(newDocument.Id, true);
                var wpfTextViewNew = await GetWpfTextViewAsync();
                wpfTextViewNew.Caret.MoveTo(new SnapshotPoint(position.Snapshot, position.Position));
                wpfTextViewNew.Caret.EnsureVisible();
                EnsureCaretVisible(wpfTextViewNew, true);

                //wpfTextViewNew.Caret.IsHidden = false;

                //wpfTextViewNew.Caret.MoveToPreferredCoordinates();
                //wpfTextViewNew.Caret.MoveToNextCaretPosition();
                //await System.Threading.Tasks.Task.Delay(200);
                //var dte = await GetDTE2Async();
                //var command = "Edit.GoTo";
                //var line = _interfaceNavigation.startPosition.GetContainingLine().LineNumber + 1;
                //dte.ExecuteCommand(command, line.ToString());

                return true;
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return true;
            }
        }

        public static void EnsureCaretVisible(ITextView textView, bool center = false)
        {
            if (textView == null)
                throw new ArgumentNullException(nameof(textView));

            var position = textView.Caret.Position.VirtualBufferPosition;
            var options = EnsureSpanVisibleOptions.ShowStart;
            if (center)
                options |= EnsureSpanVisibleOptions.AlwaysCenter;

            textView.ViewScroller.EnsureSpanVisible(new VirtualSnapshotSpan(position, position), options);
        }

        public bool NavigateTo(IEnumerable<ISymbol> symbols, Project currentProject, ISymbol selectedSymbol)
        {
            var searchSymbol = symbols.First();
            if (symbols.Contains(selectedSymbol))
            {
                searchSymbol = symbols.SkipWhile(x => x != selectedSymbol).Skip(1).FirstOrDefault() ?? searchSymbol;
            }

            return TryGoToDefinition(currentProject, searchSymbol);
        }

        private SnapshotPoint? _oldPosition = null;
        private async Task<bool> TryGoToDefinitionOrNavigateBackwardAsync(Document currentDocument, ISymbol searchSymbol, SnapshotPoint currentPosition)
        {
            var goToDefinitionSuccessful = TryGoToDefinition(currentDocument.Project, searchSymbol);

            var wpfTextView = await GetWpfTextViewAsync();
            var newPosition = wpfTextView.Caret.Position.BufferPosition;

            if (currentPosition == newPosition)
            {
                //await NavigateBackAsync();

                if (_oldPosition.HasValue)
                {
                    await GotoPositionAsync(currentDocument, _oldPosition.Value);
                    _oldPosition = null;
                }
                else
                {
                    //_oldPosition = newPosition;
                }
            }
            else
            {
                _oldPosition = currentPosition;
            }

            return goToDefinitionSuccessful;
        }

        private async System.Threading.Tasks.Task NavigateBackAsync()
        {
            try
            {
                var dte = await GetDTE2Async();
                var command = "View.NavigateBackward";
                dte.ExecuteCommand(command);
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private bool TryGoToDefinition(Project currentProject, ISymbol searchSymbol)
        {
            var result = VisualStudioWorkspace.TryGoToDefinition(searchSymbol, currentProject, CancellationToken.None);

            if (result)
            {
                return true;
            }
            else
            {
                // not found in current project ... search all projects in solution
                foreach (var project in currentProject.Solution.Projects)
                {
                    result = VisualStudioWorkspace.TryGoToDefinition(searchSymbol, project, CancellationToken.None);
                    if (result)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
