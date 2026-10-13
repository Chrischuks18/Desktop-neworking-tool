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
        "Dashboard"=>("See the team's workload, pending reviews and connected staff in one place.","Files follow Working → Submitted → Final. Open a section below to prepare work, review submissions or manage office records."),
        "Assigned Work"=>("Director and Admin assign work with instructions and deadlines; staff track and submit their assigned tasks.","Assign → prepare a working copy → submit finished work → review. Corrections return to Working Files; approval moves the submitted file to Final Files. Priority, subtasks and feedback stay with the task."),
        "Notifications"=>("Read work alerts and reminders for your signed-in account.","Assignment alerts are saved on the server. Reading them updates their server read status; clearing read alerts hides them on this computer. Chat/call reminders are local to this computer."),
        "Filing & Records"=>("Register and locate correspondence using references, categories and departments.","Director/Admin save register entries → staff search and view permitted records. Confidential entries are restricted to managers. Registering an entry does not upload or move its document."),
        "News Production"=>("Track each story's stage, assigned person, deadline and production notes.","Idea → Research → Writing → Editing → Approved → Published, then Archived when appropriate. Managers update the record; staff view/search it. Stage changes do not move office files."),
        "Meeting Minutes"=>("Keep meeting discussions, decisions and follow-up actions together.","Save minutes and action owners/dates → revise the same record as follow-up progresses. Records are shared from the server. Use Assigned Work to issue separate work assignments."),
        "Equipment Register"=>("Locate equipment and keep its asset tag, custodian and condition up to date.","Register an asset once → update custody/location/condition as it changes. Update the existing entry instead of registering the same asset again. Mark retired equipment in Condition; files are not moved."),
        "File Recovery"=>("Recover saved file versions or deleted working/shared files when a snapshot is available.","Select a saved snapshot → Restore Selected copies it back to its original working/shared location. Restore does not overwrite an existing file; rename or move the current file first when necessary."),
        "Calendar of Activities"=>("Plan office events and coverage, and view upcoming reminders.","Director/Admin add or update activities → staff view the shared calendar. Choosing a date shows its events. Calendar entries schedule reminders; they do not move work files."),
        "Working Files"=>("Prepare files in the staff member's working area before submitting them for review.","Add/edit a working file → Submit for Review moves it to Submitted Files. Files returned for correction come back here with review instructions."),
        "Submitted Files"=>("Review completed work sent by staff and decide what happens next.","Return for Correction moves the file back to the owner's Working Files. Approve to Final moves it to Final Files. Staff can recall their own submissions before approval."),
        "Shared Files"=>("Exchange useful files with everyone on the office network.","Upload → staff open/download a copy from the shared folder. This section is separate from the review workflow, and an existing shared filename cannot be overwritten."),
        "Final Files"=>("Find materials that have completed review and been approved for use.","Approved submissions arrive here from Submitted Files. Staff open/download approved copies; Backup Final Files copies them to a ZIP without moving the originals."),
        "Office Chat"=>("Message connected colleagues and make office calls over the network.","Choose an online colleague → Send privately or Call. The Director can send to everyone. Messages/audio travel through the server to connected users; Clear My Chat clears only this screen."),
        "Attendance"=>("Record the working day and review attendance or application login history.","Staff Clock In → Clock Out creates the day's attendance record. Signing in also creates login history. The Director reviews/prints these records; office files are not moved."),
        "Users"=>("Create and maintain staff names, login accounts, roles and access.","Create an account → assign its role → enable/disable or update it when needed. Account edits revoke old sessions. Deleting an account keeps its existing work files and assignment history."),
        "Settings"=>("Configure the office server, storage location, network and full backups.","Apply Storage Location can copy existing files to the chosen folder. Full backups copy the database and office files into an archive; restore is performed with the server stopped and retains rollback copies."),
        _=>("Choose a section to manage office work.","Office data is shared through the server according to your account permissions.")
    };
    private void UpdateSectionGuide(string page)
    {
        var guide=SectionGuide(page);PageGuidePurpose.Text=guide.Purpose;PageGuideMovement.Text=guide.Movement;
    }
    private void WorkspaceScroll_PreviewMouseWheel(object sender,MouseWheelEventArgs e)
    {
        // Let inner tables/notes scroll normally, then continue through the page at their boundary.
        var item=e.OriginalSource as DependencyObject;
        while(item is not null && item!=WorkspaceScroll)
        {
            if(item is ScrollViewer inner && inner.ScrollableHeight>0)
            {
                var atBoundary=e.Delta>0?inner.VerticalOffset<=0:inner.VerticalOffset>=inner.ScrollableHeight-1;
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
        PageTitle.Text=page;UpdateSectionGuide(page);DashboardSummary.Visibility=page=="Dashboard"?Visibility.Visible:Visibility.Collapsed;
        DirectorCommandCentre.Visibility=manager?Visibility.Visible:Visibility.Collapsed;
        AssignWorkPanel.Visibility=EditUserPanel.Visibility=DirectorMinutePanel.Visibility=manager?Visibility.Visible:Visibility.Collapsed;
        DirectorAttendancePanel.Visibility=manager?Visibility.Visible:Visibility.Collapsed;
        StaffClockPanel.Visibility=manager?Visibility.Collapsed:Visibility.Visible;
        CallPanel.Visibility=Visibility.Visible;SetRegisterPermissions();
        WorkspaceScroll.ScrollToTop();WorkspaceScroll.ScrollToLeftEnd();
    }
}
