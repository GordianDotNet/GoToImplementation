using EnvDTE;
using EnvDTE80;
using Microsoft;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Threading.Tasks;

namespace GoToImplementation
{
    public partial class VisualStudioServices
    {
        public AsyncPackage Package
        {
            get;
            private set;
        }

        public IServiceProvider ServiceProvider => (IServiceProvider)Package;

        public IComponentModel ComponentModel => ServiceProvider.GetService(typeof(SComponentModel)) as IComponentModel;

        public VisualStudioWorkspace VisualStudioWorkspace => ComponentModel.GetService<VisualStudioWorkspace>();

        public async Task<IWpfTextView> GetWpfTextViewAsync()
        {
            var obj = await Package.GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager ?? throw new ArgumentNullException("SVsTextManager service not found");
            IVsTextView activeView = null;
            ErrorHandler.ThrowOnFailure(obj.GetActiveView(1, null, out activeView));
            return ComponentModel.GetService<IVsEditorAdaptersFactoryService>().GetWpfTextView(activeView);
        }   

        public async Task<DTE2> GetDTE2Async()
        {
            return await Package.GetServiceAsync(typeof(DTE)) as DTE2;
        }

        public IVsUIShell6 VsUIShell6
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return ServiceProvider.GetService(typeof(IVsUIShell)) as IVsUIShell6;
            }
        }

        public IVsUIShell VsUIShell
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return ServiceProvider.GetService(typeof(SVsUIShell)) as IVsUIShell;
            }
        }

        public VisualStudioServices(AsyncPackage package)
        {
            Package = package;
        }
    }
}