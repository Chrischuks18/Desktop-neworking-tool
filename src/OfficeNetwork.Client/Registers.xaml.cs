using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using Control = System.Windows.Controls.Control;
using TextBox = System.Windows.Controls.TextBox;
using System.Globalization;
using OfficeNetwork.Shared;
namespace OfficeNetwork.Client;

public partial class MainWindow
{
    private bool CanManageRegisters=>_currentUser?.Role is OfficeRole.Director or OfficeRole.Admin;
    private void ConfigureRegisterEditor(Grid editor)
    {
        editor.IsEnabled=true;
        foreach(var control in editor.Children.OfType<StackPanel>().SelectMany(panel=>panel.Children.OfType<Control>()))
        {
            if(control is TextBox text)text.IsReadOnly=!CanManageRegisters;
            else control.IsEnabled=CanManageRegisters;
        }
    }
    private void SetRegisterPermissions()
    {
        ConfigureRegisterEditor(ProductionEditor);
        ProductionSaveButton.Visibility=ProductionNewButton.Visibility=CanManageRegisters?Visibility.Visible:Visibility.Collapsed;
        ConfigureRegisterEditor(MinutesEditor);
        MinutesSaveButton.Visibility=MinutesNewButton.Visibility=CanManageRegisters?Visibility.Visible:Visibility.Collapsed;
        ConfigureRegisterEditor(EquipmentEditor);
        EquipmentSaveButton.Visibility=EquipmentNewButton.Visibility=CanManageRegisters?Visibility.Visible:Visibility.Collapsed;
    }
    private void ResetRegisters()
    {
        _productionItems=[];ProductionList.ItemsSource=null;ProductionSearch.Clear();ResetProductionForm();ProductionStatus.Text="";
        _minutesItems=[];MinutesList.ItemsSource=null;MinutesSearch.Clear();ResetMinutesForm();MinutesStatus.Text="";
        _equipmentItems=[];EquipmentList.ItemsSource=null;EquipmentSearch.Clear();ResetEquipmentForm();EquipmentStatus.Text="";
        SetRegisterPermissions();
    }
    private static DateTime? ReadRegisterDate(string value)=>DateTime.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)?date:null;
    private static string RegisterDate(DatePicker picker)=>picker.SelectedDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"";
    private ProductionItem[] _productionItems=[];
    private Guid? _productionEditingId;
    private string _productionOriginalDeadline="";
    private string ProductionDeadlineValue()=>ProductionDeadline.SelectedDate.HasValue?RegisterDate(ProductionDeadline):ReadRegisterDate(_productionOriginalDeadline) is null?_productionOriginalDeadline:"";
    private async Task LoadProductionAsync()
    {
        var session=_currentUser;if(session is null)return;
        try
        {
            var items=await _http.GetFromJsonAsync<ProductionItem[]>($"{LoginServerAddress.Text.TrimEnd('/')}/api/production")??[];
            if(!ReferenceEquals(_currentUser,session))return;
            _productionItems=items;FilterProduction();
            ProductionStatus.Text=$"{items.Length} record(s). "+(CanManageRegisters?"Select a record to edit, or choose New Record.":"Select a record to view its details. Only Director and Admin accounts can make changes.");
        }
        catch(Exception ex){if(ReferenceEquals(_currentUser,session))ProductionStatus.Text="Unable to load records: "+ex.Message;}
    }
    private void FilterProduction()
    {
        var query=ProductionSearch.Text.Trim();
        ProductionList.ItemsSource=_productionItems.Where(x=>string.Join(" ",x.Title,x.Stage,x.AssignedTo,x.Deadline,x.Notes).Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    private void ProductionSearch_Changed(object sender,TextChangedEventArgs e){if(IsLoaded)FilterProduction();}
    private async void RefreshProduction_Click(object sender,RoutedEventArgs e)=>await LoadProductionAsync();
    private void NewProduction_Click(object sender,RoutedEventArgs e){ResetProductionForm();ProductionStatus.Text="Enter the new record and choose Save Record.";}
    private void ResetProductionForm()
    {
        _productionOriginalDeadline="";
        _productionEditingId=null;ProductionList.SelectedItem=null;ProductionSaveButton.Content="Save Record";
        ProductionTitle.Clear();
        ProductionStage.Text="Idea";
        ProductionAssignedTo.Clear();
        ProductionDeadline.SelectedDate=null;
        ProductionNotes.Clear();
    }
    private void ProductionList_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(ProductionList.SelectedItem is not ProductionItem item)return;
        _productionOriginalDeadline=item.Deadline;
        _productionEditingId=item.Id;ProductionSaveButton.Content="Update Record";
        ProductionTitle.Text=item.Title;
        ProductionStage.Text=item.Stage;
        ProductionAssignedTo.Text=item.AssignedTo;
        ProductionDeadline.SelectedDate=ReadRegisterDate(item.Deadline);
        ProductionNotes.Text=item.Notes;
    }
    private async void SaveProduction_Click(object sender,RoutedEventArgs e)
    {
        var session=_currentUser;if(session is null || !CanManageRegisters)return;
        var id=_productionEditingId;
        var item=new ProductionItem(id??Guid.Empty,ProductionTitle.Text.Trim(),ProductionStage.Text.Trim(),ProductionAssignedTo.Text.Trim(),ProductionDeadlineValue(),ProductionNotes.Text.Trim());
        ProductionEditor.IsEnabled=false;ProductionSaveButton.IsEnabled=false;ProductionNewButton.IsEnabled=false;
        ProductionStatus.Text="Saving…";
        try
        {
            var url=$"{LoginServerAddress.Text.TrimEnd('/')}/api/production";
            using var response=id.HasValue?await _http.PutAsJsonAsync($"{url}/{id}",item):await _http.PostAsJsonAsync(url,item);
            if(!ReferenceEquals(_currentUser,session))return;
            if(!response.IsSuccessStatusCode){ProductionStatus.Text="Could not save: "+await response.Content.ReadAsStringAsync();return;}
            await LoadProductionAsync();ResetProductionForm();
            ProductionStatus.Text=id.HasValue?"Record updated successfully.":"Record saved successfully.";
        }
        catch(Exception ex){if(ReferenceEquals(_currentUser,session))ProductionStatus.Text="Could not save: "+ex.Message;}
        finally{ConfigureRegisterEditor(ProductionEditor);ProductionSaveButton.IsEnabled=true;ProductionNewButton.IsEnabled=true;}
    }
    private MeetingMinute[] _minutesItems=[];
    private Guid? _minutesEditingId;
    private async Task LoadMinutesAsync()
    {
        var session=_currentUser;if(session is null)return;
        try
        {
            var items=await _http.GetFromJsonAsync<MeetingMinute[]>($"{LoginServerAddress.Text.TrimEnd('/')}/api/meeting-minutes")??[];
            if(!ReferenceEquals(_currentUser,session))return;
            _minutesItems=items;FilterMinutes();
            MinutesStatus.Text=$"{items.Length} record(s). "+(CanManageRegisters?"Select a record to edit, or choose New Record.":"Select a record to view its details. Only Director and Admin accounts can make changes.");
        }
        catch(Exception ex){if(ReferenceEquals(_currentUser,session))MinutesStatus.Text="Unable to load records: "+ex.Message;}
    }
    private void FilterMinutes()
    {
        var query=MinutesSearch.Text.Trim();
        MinutesList.ItemsSource=_minutesItems.Where(x=>string.Join(" ",x.MeetingTitle,x.MeetingDate,x.RecordedBy,x.Minutes,x.ActionItems).Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    private void MinutesSearch_Changed(object sender,TextChangedEventArgs e){if(IsLoaded)FilterMinutes();}
    private async void RefreshMinutes_Click(object sender,RoutedEventArgs e)=>await LoadMinutesAsync();
    private void NewMinutes_Click(object sender,RoutedEventArgs e){ResetMinutesForm();MinutesStatus.Text="Enter the new record and choose Save Record.";}
    private void ResetMinutesForm()
    {
        _minutesEditingId=null;MinutesList.SelectedItem=null;MinutesSaveButton.Content="Save Record";
        MinutesMeetingTitle.Clear();
        MinutesMeetingDate.SelectedDate=DateTime.Today;
        MinutesRecordedBy.Text=_currentUser?.DisplayName??"";
        MinutesMinutes.Clear();
        MinutesActionItems.Clear();
    }
    private void MinutesList_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(MinutesList.SelectedItem is not MeetingMinute item)return;
        _minutesEditingId=item.Id;MinutesSaveButton.Content="Update Record";
        MinutesMeetingTitle.Text=item.MeetingTitle;
        MinutesMeetingDate.SelectedDate=ReadRegisterDate(item.MeetingDate);
        MinutesRecordedBy.Text=item.RecordedBy;
        MinutesMinutes.Text=item.Minutes;
        MinutesActionItems.Text=item.ActionItems;
    }
    private async void SaveMinutes_Click(object sender,RoutedEventArgs e)
    {
        var session=_currentUser;if(session is null || !CanManageRegisters)return;
        var id=_minutesEditingId;
        var item=new MeetingMinute(id??Guid.Empty,MinutesMeetingTitle.Text.Trim(),RegisterDate(MinutesMeetingDate),MinutesRecordedBy.Text.Trim(),MinutesMinutes.Text.Trim(),MinutesActionItems.Text.Trim());
        MinutesEditor.IsEnabled=false;MinutesSaveButton.IsEnabled=false;MinutesNewButton.IsEnabled=false;
        MinutesStatus.Text="Saving…";
        try
        {
            var url=$"{LoginServerAddress.Text.TrimEnd('/')}/api/meeting-minutes";
            using var response=id.HasValue?await _http.PutAsJsonAsync($"{url}/{id}",item):await _http.PostAsJsonAsync(url,item);
            if(!ReferenceEquals(_currentUser,session))return;
            if(!response.IsSuccessStatusCode){MinutesStatus.Text="Could not save: "+await response.Content.ReadAsStringAsync();return;}
            await LoadMinutesAsync();ResetMinutesForm();
            MinutesStatus.Text=id.HasValue?"Record updated successfully.":"Record saved successfully.";
        }
        catch(Exception ex){if(ReferenceEquals(_currentUser,session))MinutesStatus.Text="Could not save: "+ex.Message;}
        finally{ConfigureRegisterEditor(MinutesEditor);MinutesSaveButton.IsEnabled=true;MinutesNewButton.IsEnabled=true;}
    }
    private EquipmentItem[] _equipmentItems=[];
    private Guid? _equipmentEditingId;
    private async Task LoadEquipmentAsync()
    {
        var session=_currentUser;if(session is null)return;
        try
        {
            var items=await _http.GetFromJsonAsync<EquipmentItem[]>($"{LoginServerAddress.Text.TrimEnd('/')}/api/equipment")??[];
            if(!ReferenceEquals(_currentUser,session))return;
            _equipmentItems=items;FilterEquipment();
            EquipmentStatus.Text=$"{items.Length} record(s). "+(CanManageRegisters?"Select a record to edit, or choose New Record.":"Select a record to view its details. Only Director and Admin accounts can make changes.");
        }
        catch(Exception ex){if(ReferenceEquals(_currentUser,session))EquipmentStatus.Text="Unable to load records: "+ex.Message;}
    }
    private void FilterEquipment()
    {
        var query=EquipmentSearch.Text.Trim();
        EquipmentList.ItemsSource=_equipmentItems.Where(x=>string.Join(" ",x.AssetTag,x.Name,x.Location,x.Custodian,x.Condition,x.Notes).Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    private void EquipmentSearch_Changed(object sender,TextChangedEventArgs e){if(IsLoaded)FilterEquipment();}
    private async void RefreshEquipment_Click(object sender,RoutedEventArgs e)=>await LoadEquipmentAsync();
    private void NewEquipment_Click(object sender,RoutedEventArgs e){ResetEquipmentForm();EquipmentStatus.Text="Enter the new record and choose Save Record.";}
    private void ResetEquipmentForm()
    {
        _equipmentEditingId=null;EquipmentList.SelectedItem=null;EquipmentSaveButton.Content="Save Record";
        EquipmentAssetTag.Clear();
        EquipmentName.Clear();
        EquipmentLocation.Clear();
        EquipmentCustodian.Clear();
        EquipmentCondition.Text="Good";
        EquipmentNotes.Clear();
    }
    private void EquipmentList_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(EquipmentList.SelectedItem is not EquipmentItem item)return;
        _equipmentEditingId=item.Id;EquipmentSaveButton.Content="Update Record";
        EquipmentAssetTag.Text=item.AssetTag;
        EquipmentName.Text=item.Name;
        EquipmentLocation.Text=item.Location;
        EquipmentCustodian.Text=item.Custodian;
        EquipmentCondition.Text=item.Condition;
        EquipmentNotes.Text=item.Notes;
    }
    private async void SaveEquipment_Click(object sender,RoutedEventArgs e)
    {
        var session=_currentUser;if(session is null || !CanManageRegisters)return;
        var id=_equipmentEditingId;
        var item=new EquipmentItem(id??Guid.Empty,EquipmentAssetTag.Text.Trim(),EquipmentName.Text.Trim(),EquipmentLocation.Text.Trim(),EquipmentCustodian.Text.Trim(),EquipmentCondition.Text.Trim(),EquipmentNotes.Text.Trim());
        EquipmentEditor.IsEnabled=false;EquipmentSaveButton.IsEnabled=false;EquipmentNewButton.IsEnabled=false;
        EquipmentStatus.Text="Saving…";
        try
        {
            var url=$"{LoginServerAddress.Text.TrimEnd('/')}/api/equipment";
            using var response=id.HasValue?await _http.PutAsJsonAsync($"{url}/{id}",item):await _http.PostAsJsonAsync(url,item);
            if(!ReferenceEquals(_currentUser,session))return;
            if(!response.IsSuccessStatusCode){EquipmentStatus.Text="Could not save: "+await response.Content.ReadAsStringAsync();return;}
            await LoadEquipmentAsync();ResetEquipmentForm();
            EquipmentStatus.Text=id.HasValue?"Record updated successfully.":"Record saved successfully.";
        }
        catch(Exception ex){if(ReferenceEquals(_currentUser,session))EquipmentStatus.Text="Could not save: "+ex.Message;}
        finally{ConfigureRegisterEditor(EquipmentEditor);EquipmentSaveButton.IsEnabled=true;EquipmentNewButton.IsEnabled=true;}
    }
}
