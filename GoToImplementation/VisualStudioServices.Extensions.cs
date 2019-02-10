using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.Linq;
using Microsoft.CodeAnalysis.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Threading;

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

            var bufferPosition = wpfTextView.Caret.Position.BufferPosition;
            var document = bufferPosition.Snapshot.GetOpenDocumentInCurrentContextWithChanges();
            if (document == null)
            {
                return false;
            }

            bufferPosition = wpfTextView.Caret.Position.BufferPosition;
            int position = bufferPosition.Position;
            var semanticModel = await document.GetSemanticModelAsync();
            var selectedSymbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, document.Project.Solution.Workspace);
            if (selectedSymbol == null)
            {
                ShowMessageBox("No symbol under the caret.");
                return false;
            }
            var implSymbols = await SymbolFinder.FindImplementationsAsync(selectedSymbol, document.Project.Solution);
            if (implSymbols.Any())
            {
                NavigateTo(implSymbols, document.Project);
                return true;
            }
            else
            {
                ShowMessageBox("No implementations found.");
                return false;
            }
        }

        public bool NavigateTo(IEnumerable<ISymbol> symbols, Project currentProject)
        {
            var result = VisualStudioWorkspace.TryGoToDefinition(symbols.First(), currentProject, CancellationToken.None);

            if (result)
            {
                return true;
            }
            else
            {
                // not found in current project ... search all projects in solution
                foreach (var project in currentProject.Solution.Projects)
                {
                    result = VisualStudioWorkspace.TryGoToDefinition(symbols.First(), project, CancellationToken.None);
                    if (result)
                    {
                        return true;
                    }
                }
            }

            return true;
        }
    }
}
