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

            currentPosition = wpfTextView.Caret.Position.BufferPosition;
            int position = currentPosition.Position;
            var semanticModel = await document.GetSemanticModelAsync();

            // find symbol at caret position
            var selectedSymbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, document.Project.Solution.Workspace);
            if (selectedSymbol == null)
            {
                // no symbol selected => we can't help
                return false;
            }

            // search implementation only if method or property symbol
            if (selectedSymbol is IMethodSymbol || selectedSymbol is IPropertySymbol)
            {
                try
                {
                    // prio 1: we're assuming it's an interface method/property => find implementations
                    var implSymbols = await SymbolFinder.FindImplementationsAsync(selectedSymbol, document.Project.Solution);
                    if (implSymbols.Any())
                    {
                        NavigateTo(implSymbols, document.Project, selectedSymbol);
                        return true;
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
            return await TryGoToDefinitionOrNavigateBackwardAsync(document.Project, selectedSymbol, currentPosition);
        }

        //private (SnapshotPoint position, ISymbol symbol) _lastInterface;

        //private Stack<(SnapshotPoint position, ISymbol symbol)> _positionHistory = new Stack<(SnapshotPoint position, ISymbol symbol)>();

        public bool NavigateTo(IEnumerable<ISymbol> symbols, Project currentProject, ISymbol selectedSymbol)
        {
            var searchSymbol = symbols.First();
            if (symbols.Contains(selectedSymbol))
            {
                searchSymbol = symbols.SkipWhile(x => x != selectedSymbol).Skip(1).FirstOrDefault() ?? searchSymbol;
            }

            return TryGoToDefinition(currentProject, searchSymbol);
        }

        private async Task<bool> TryGoToDefinitionOrNavigateBackwardAsync(Project currentProject, ISymbol searchSymbol, SnapshotPoint currentPosition)
        {
            var goToDefinitionSuccessful = TryGoToDefinition(currentProject, searchSymbol);

            var wpfTextView = await GetWpfTextViewAsync();
            var newPosition = wpfTextView.Caret.Position.BufferPosition;

            if (currentPosition == newPosition)
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

                ////if (_positionHistory.Count > 0)
                //{
                //    //var (oldPosition, oldSymbol) = _positionHistory.Pop();
                //    //var newDocument = newPosition.Snapshot.GetOpenDocumentInCurrentContextWithChanges();
                //    //var oldDocument = oldPosition.Snapshot.GetOpenDocumentInCurrentContextWithChanges();

                //    //if (newDocument.Id != oldDocument.Id)
                //    //{
                //    //    VisualStudioWorkspace.OpenDocument(oldDocument.Id, true);
                //    //}
                //    //else
                //    //{
                //        try
                //        {
                //            //wpfTextView.Caret.MoveTo(wpfTextView.GetTextViewLineContainingBufferPosition(wpfTextView.TextSnapshot.GetLineFromPosition(wpfTextView.TextSnapshot.GetText().IndexOf(fn)).Start));
                //            //System.Windows.Forms.SendKeys.Send("{RIGHT}");
                //            //wpfTextView.Caret.MoveTo(new SnapshotPoint(wpfTextView.TextSnapshot, oldPosition.Position));//.Select(new SnapshotSpan(oldPosition, 1), false);
                //            //System.Windows.Forms.SendKeys.Send("^(-)");
                //            var dte = await GetDTE2Async();
                //            //var i = dte.Commands.Count;
                //            var command = "View.NavigateBackward";
                //            dte.ExecuteCommand(command);
                //        }
                //        catch (System.Exception ex)
                //        {
                //            Debug.WriteLine(ex.Message);
                //        }
                //    //}

                //}
                ////else
                ////{
                ////    _positionHistory.Push((currentPosition, searchSymbol));
                ////}
            }
            //else
            //{
            //    _positionHistory.Push((currentPosition, searchSymbol));
            //}

            return goToDefinitionSuccessful;
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
