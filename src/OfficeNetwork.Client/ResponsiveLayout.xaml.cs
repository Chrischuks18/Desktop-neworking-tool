using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OfficeNetwork.Shared;
using Button=System.Windows.Controls.Button;
using ScrollViewer=System.Windows.Controls.ScrollViewer;

namespace OfficeNetwork.Client;

public partial class MainWindow
{
    internal static readonly string[] SectionPages=["Dashboard","Assigned Work","Notifications","Filing & Records","News Production","Meeting Minutes","Equipment Register","File Recovery","Calendar of Activities","Working Files","Submitted Files","Shared Files","Final Files","Office Chat","Attendance","Users","Settings"];
    internal static (string Purpose,string Movement) SectionGuide(string page)=>page switch
    {
        "Dashboard"=>("See workload, pending reviews and connected staff at a glance.","Files follow Working → Submitted → Final. Open a section to prepare work, review submissions or manage records."),
        "Assigned Work"=>("Assign tasks and track instructions, deadlines, progress and feedback.","Assign → prepare → submit → review. Corrections return files to Working; approval moves them to Final. Managers assign; staff prepare and submit."),
        "Notifications"=>("Read alerts and reminders for your signed-in account.","Assignment alerts and read status are saved on the server. Clear Read hides them on this PC. Chat/call reminders are local."),
        "Filing & Records"=>("Register correspondence by reference, category and department.","Managers save entries; staff view permitted records. Confidential entries are manager-only. Registering an entry does not upload its document."),
        "News Production"=>("Track story stages, assigned staff, deadlines and notes.","Idea → Research → Writing → Editing → Approved → Published → Archived. Managers update records; staff view/search them. Stages do not move files."),
        "Meeting Minutes"=>("Record discussions, decisions and follow-up actions.","Save minutes and action owners/dates → update the same record as follow-up progresses. Use Assigned Work to issue separate tasks."),
        "Equipment Register"=>("Track asset tags, location, custody and condition.","Register once → update the same asset when custody or condition changes. Keep asset tags unique; mark retired assets in Condition."),
        "File Recovery"=>("Recover saved versions or deleted working/shared files.","Restore Selected copies a snapshot to its working/shared location. It will not overwrite an existing filename; ask a manager if it is occupied."),
        "Calendar of Activities"=>("Plan office events and coverage, and view reminders.","Managers save activities → staff view the shared calendar. Select a date for its events. Entries schedule reminders, not file movement."),
        "Working Files"=>("Prepare staff files before review.","Add/edit → Submit for Review moves the file to Submitted. Returned corrections come back here with instructions."),
        "Submitted Files"=>("Review finished work and choose its next step.","Return for Correction → owner's Working Files. Approve to Final → Final Files. Staff may recall their own unapproved submissions."),
        "Shared Files"=>("Exchange useful files with office colleagues.","Upload → colleagues open/download a copy. This is separate from review, and existing shared filenames cannot be overwritten."),
        "Final Files"=>("Access materials approved for use.","Approved submissions arrive from Submitted Files. Staff download copies. Backup Final Files creates a ZIP and keeps the originals."),
        "Office Chat"=>("Message connected colleagues or make office calls.","Select a colleague → Send privately or Call. The Director can message everyone. Clear My Chat clears only this screen."),
        "Attendance"=>("Record working hours and review attendance or login history.","Clock In → Clock Out saves the workday. Signing in also records login history. The Director reviews/prints records; files do not move."),
        "Users"=>("Maintain names, login accounts, roles and access.","Create → assign a role → update or disable when needed. Edits revoke old sessions. Account deletion retains work files and assignment history."),
        "Settings"=>("Configure office storage, networking and full backups.","Storage changes can copy existing files. Backups copy data into an archive. Restore runs with the server stopped and keeps rollback copies."),
        _=>("Choose a section to manage office work.","Office data is shared through the server according to your account permissions.")
    };
    private void UpdateSectionGuide(string page)
    {
        var guide=SectionGuide(page);PageGuidePurpose.Text=guide.Purpose;PageGuideMovement.Text=guide.Movement;
    }
    private void WorkspaceScroll_SizeChanged(object sender,SizeChangedEventArgs e)
    {
        var narrow=WorkspaceScroll.ActualWidth<700;
        Grid.SetColumn(WorkspaceAccount,narrow?0:1);Grid.SetRow(WorkspaceAccount,narrow?1:0);
        Grid.SetColumnSpan(WorkspaceAccount,narrow?2:1);Grid.SetColumnSpan(WorkspaceHeading,narrow?2:1);
        WorkspaceAccount.HorizontalAlignment=narrow?HorizontalAlignment.Left:HorizontalAlignment.Right;
        WorkspaceAccount.Margin=narrow?new Thickness(0,8,0,0):new Thickness();
        WorkspaceAccount.MaxWidth=narrow?Math.Max(180,WorkspaceScroll.ActualWidth-20):double.PositiveInfinity;
    }
    private void WorkspaceScroll_PreviewMouseWheel(object sender,MouseWheelEventArgs e)
    {
        // Let inner tables/notes scroll normally, then continue through the page at their boundary.
        var item=e.OriginalSource as DependencyObject;
        while(item is not null && item!=WorkspaceScroll)
        {
            if(item is ScrollViewer inner && inner.ScrollableHeight>0)
            {
                var atBoundary=e.Delta>0?inner.VerticalOffset<=0:inner.VerticalOffset>=inner.ScrollableHeight-0.01;
                if(!atBoundary)return;
                break;
            }
            item=item is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(item):LogicalTreeHelper.GetParent(item);
        }
        if(WorkspaceScroll.ScrollableHeight>0)
        {
            WorkspaceScroll.ScrollToVerticalOffset(WorkspaceScroll.VerticalOffset-e.Delta);
            e.Handled=true;
        }
    }
    internal void PreviewSectionForLayoutChecks(string page,bool manager)
    {
        if(!_layoutPreview)throw new InvalidOperationException("Layout previews require the isolated preview constructor.");
        _currentUser=new(Guid.NewGuid(),"layout-sample","Layout sample",manager?OfficeRole.Director:OfficeRole.Editor,"not-a-real-session");
        CurrentUserName.Text="Layout sample";CurrentUserRole.Text=manager?"Director":"Editor";
        LoginOverlay.Visibility=Visibility.Collapsed;
        foreach(var name in new[]{"DashboardContent","AssignmentsContent","NotificationsContent","FilingContent","ProductionContent","MinutesContent","EquipmentContent","RecoveryContent","CalendarContent","SectionContent","AttendanceContent","UsersContent","SettingsContent"})
            ((UIElement)FindName(name)).Visibility=Visibility.Collapsed;
        var target=page switch
        {
            "Dashboard" or "Office Chat"=>"DashboardContent","Assigned Work"=>"AssignmentsContent","Notifications"=>"NotificationsContent","Filing & Records"=>"FilingContent",
            "News Production"=>"ProductionContent","Meeting Minutes"=>"MinutesContent","Equipment Register"=>"EquipmentContent","File Recovery"=>"RecoveryContent",
            "Calendar of Activities"=>"CalendarContent","Working Files" or "Submitted Files" or "Final Files" or "Shared Files"=>"SectionContent","Attendance"=>"AttendanceContent","Users"=>"UsersContent",_=>"SettingsContent"
        };
        ((UIElement)FindName(target)).Visibility=Visibility.Visible;
        PageTitle.Text=page;if(target=="SectionContent")SectionHeading.Text=page;UpdateSectionGuide(page);DashboardSummary.Visibility=page=="Dashboard"?Visibility.Visible:Visibility.Collapsed;
        DirectorCommandCentre.Visibility=manager?Visibility.Visible:Visibility.Collapsed;
        AssignWorkPanel.Visibility=EditUserPanel.Visibility=DirectorMinutePanel.Visibility=manager?Visibility.Visible:Visibility.Collapsed;
        DirectorAttendancePanel.Visibility=manager?Visibility.Visible:Visibility.Collapsed;
        StaffClockPanel.Visibility=manager?Visibility.Collapsed:Visibility.Visible;
        CallPanel.Visibility=Visibility.Visible;SetRegisterPermissions();
        WorkspaceScroll.ScrollToTop();WorkspaceScroll.ScrollToLeftEnd();
    }
}
