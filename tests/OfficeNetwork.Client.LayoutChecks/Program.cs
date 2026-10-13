using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OfficeNetwork.Client;
using OfficeNetwork.Shared;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            var directory=args.FirstOrDefault()??"layout-checks";
            Directory.CreateDirectory(directory);
            var results=new List<object>();
            var window=new MainWindow(true);
            window.Show();
            foreach(var size in new[]{new Size(800,480),new Size(640,400),new Size(1024,600),new Size(1280,720)})
            foreach(var manager in new[]{true,false})
            foreach(var page in MainWindow.SectionPages)
            {
                window.Width=size.Width;window.Height=size.Height;
                window.PreviewSectionForLayoutChecks(page,manager);
                var layout=(Grid)window.FindName("WorkspaceLayout");
                var scroll=(ScrollViewer)window.FindName("WorkspaceScroll");
                // Many records and long multi-line details stress the same controls used in normal pages.
                foreach(var list in Descendants(layout).OfType<ItemsControl>().Where(x=>x is ListView or ListBox or DataGrid))
                    list.ItemsSource=Enumerable.Range(1,80).Select(i=>new
                    {
                        Id=Guid.NewGuid(),Title=$"Layout sample {i}: a long descriptive title for office work",MeetingTitle=$"Meeting {i}",
                        Name=$"Sample file {i}.docx",DisplayName=$"Layout sample {i}",UserName=$"sample-{i}",Role=OfficeRole.Editor,IsEnabled=true,
                        Stage="Editing",AssignedTo="Layout sample",Deadline="2026-10-20",MeetingDate="2026-10-13",RecordedBy="Layout sample",
                        AssetTag=$"CAM-{i}",Location="Studio",Custodian="Layout sample",Condition="Good",OwnerUserName="sample",Status="Pending",
                        Instructions="Review the draft, resolve comments and prepare the finished copy.",Notes="Detailed notes for the selected record.",
                        Reference=$"REF-{i}",Category="Internal",Department="Editorial",Confidential=false,
                        At=DateTimeOffset.UtcNow,Message="A sample alert describing the next action.",IsRead=false,
                        WorkDate=DateTime.Today,ClockIn=DateTimeOffset.UtcNow,ClockOut=(DateTimeOffset?)null,LoginDate="13 Oct 2026",LoginTime="8:45 AM",
                        FileName="sample.docx",Folder="WorkingFiles",Owner="sample",Actor="Layout sample",SavedAt=DateTimeOffset.UtcNow,Reason="Saved version",Deleted=false
                    }).ToArray();
                foreach(var text in Descendants(layout).OfType<TextBox>().Where(x=>x.AcceptsReturn))
                    text.Text=string.Join(Environment.NewLine,Enumerable.Repeat("Long details remain readable and scrollable without hiding the page's buttons.",20));
                window.UpdateLayout();Pump();window.UpdateLayout();
                var guide=MainWindow.SectionGuide(page);
                if(string.IsNullOrWhiteSpace(guide.Purpose)||string.IsNullOrWhiteSpace(guide.Movement))throw new Exception($"Missing guide: {page}");
                if(scroll.ViewportHeight<=0 || scroll.ViewportWidth<=0)throw new Exception($"Missing viewport: {page}");
                foreach(var button in Descendants(layout).OfType<Button>().Where(x=>x.IsVisible && x.Content is string && x.ActualWidth>0))
                {
                    var position=button.TranslatePoint(new Point(),layout);
                    if(position.X< -1 || position.X+button.ActualWidth>layout.ActualWidth+2 || position.Y< -1 || position.Y+button.ActualHeight>layout.ActualHeight+2)
                        throw new Exception($"Clipped action {button.Content} in {page} at {size.Width}x{size.Height}: {position}, button {button.ActualWidth}x{button.ActualHeight}, page {layout.ActualWidth}x{layout.ActualHeight}");
                }
                if(size.Width==800 && manager && page is "Assigned Work" or "Users" or "Settings" or "Working Files" or "Calendar of Activities" or "Meeting Minutes")
                {
                    var name=page.Replace(' ','-');
                    Save(window,$"{directory}/{name}-top.png");
                    scroll.ScrollToBottom();Pump();window.UpdateLayout();Save(window,$"{directory}/{name}-bottom.png");
                    scroll.ScrollToRightEnd();Pump();window.UpdateLayout();Save(window,$"{directory}/{name}-bottom-right.png");
                }
                results.Add(new {page,manager,width=size.Width,height=size.Height,scroll.ViewportWidth,scroll.ViewportHeight,scroll.ExtentWidth,scroll.ExtentHeight,scroll.ScrollableHeight,scroll.ScrollableWidth});
            }
            File.WriteAllText(Path.Combine(directory,"layout-results.json"),System.Text.Json.JsonSerializer.Serialize(results,new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));
            window.Close();app.Shutdown();
            Console.WriteLine($"Passed {results.Count} section/role/window-size layout checks; all visible action buttons fit within scrollable content. Screenshots saved in {directory}.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);yield return child;
            foreach(var item in Descendants(child))yield return item;
        }
    }
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    private static void Save(MainWindow window,string path)
    {
        var visual=(FrameworkElement)window.Content;
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),(int)Math.Ceiling(visual.ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(path);encoder.Save(file);
    }
}
