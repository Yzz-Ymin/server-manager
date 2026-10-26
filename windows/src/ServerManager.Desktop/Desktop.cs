using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace ServerManager {
internal static class Ui {
    internal static Brush ColorBrush(string color){return (Brush)new BrushConverter().ConvertFromString(color);}
    internal static Brush Ink=ColorBrush("#DBE9F7"),Muted=ColorBrush("#829BB5"),Blue=ColorBrush("#298CEB"),Cyan=ColorBrush("#68D8FF"),Panel=ColorBrush("#0D1D30"),Line=ColorBrush("#203850");
    internal static Button Button(string text,RoutedEventHandler click,bool primary=false){var b=new Button{Content=text,Padding=new Thickness(17,10,17,10),Margin=new Thickness(0,0,9,0),MinHeight=40,Cursor=System.Windows.Input.Cursors.Hand};if(primary){b.Background=new LinearGradientBrush(Color.FromRgb(38,140,237),Color.FromRgb(33,104,211),0);b.Foreground=Brushes.White;b.BorderBrush=ColorBrush("#4EB9FF");}b.Click+=click;return b;}
    internal static TextBlock Text(string text,int size=14){return new TextBlock{Text=text,FontSize=size,Foreground=Ink,TextWrapping=TextWrapping.Wrap};}
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    internal static void Window(Window w,string title,int width,int height){w.Title=title;w.Width=width;w.Height=height;w.MinWidth=width-80;w.MinHeight=height-80;w.FontFamily=new FontFamily("Microsoft YaHei UI");w.FontSize=14;w.Foreground=Ink;w.Background=ColorBrush("#091422");w.WindowStartupLocation=WindowStartupLocation.CenterScreen;w.Icon=BitmapFrame.Create(new Uri(Path.Combine(Engine.Bin,"AppIcon.ico")));
        using(var stream=typeof(Ui).Assembly.GetManifestResourceStream("ServerManager.Theme.xaml"))w.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        w.SourceInitialized+=(s,e)=>{try{var h=new WindowInteropHelper(w).Handle;int dark=1,caption=0x221409,text=0xF7E9DB;DwmSetWindowAttribute(h,20,ref dark,4);DwmSetWindowAttribute(h,35,ref caption,4);DwmSetWindowAttribute(h,36,ref text,4);}catch(DllNotFoundException){}};
    }
    internal static Border Card(UIElement child){return new Border{Background=Panel,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(20),Child=child};}
    internal static bool Confirm(Window owner,string title,string message,string accept="确认"){
        var w=new Window{Owner=owner,ShowInTaskbar=false};Window(w,title,660,470);w.WindowStartupLocation=WindowStartupLocation.CenterOwner;var root=new DockPanel{Margin=new Thickness(28)};var heading=Text(title,23);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(0,0,0,20);DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,22,0,0)};buttons.Children.Add(Button("取消",(s,e)=>w.DialogResult=false));buttons.Children.Add(Button(accept,(s,e)=>w.DialogResult=true,true));DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);root.Children.Add(new ScrollViewer{Content=Text(message,14),VerticalScrollBarVisibility=ScrollBarVisibility.Auto});w.Content=root;return w.ShowDialog()==true;
    }
    internal static void Notice(Window owner,string title,string message){Result(owner,ServerManager.Result.Error("local",title,message));}
    internal static void Error(Window owner,Exception ex){var ae=ex as AccessException;Notice(owner,ae==null?"本机错误":ae.Code,ae==null?"操作未完成，请检查本机文件权限或重试。":ae.Message);}
    internal static void Result(Window owner,Result r){var w=new Window{Owner=owner,ShowInTaskbar=false};Window(w,r.success?"验证完成":"连接诊断",740,500);w.WindowStartupLocation=WindowStartupLocation.CenterOwner;var root=new DockPanel{Margin=new Thickness(28)};
        var bottom=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};bottom.Children.Add(Button("完成",(s,e)=>w.Close(),true));DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        var header=new StackPanel{Margin=new Thickness(0,0,0,22)};var badge=Text(r.success?"●  CONNECTION VERIFIED":"●  CONNECTION REPORT",11);badge.Foreground=r.success?ColorBrush("#63E2BD"):ColorBrush("#FFBC85");badge.FontFamily=new FontFamily("Consolas");header.Children.Add(badge);var title=Text(r.message,22);title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(0,12,0,12);header.Children.Add(title);var detail=Text("阶段  "+r.stage+"     /     "+r.code+(r.remoteExitCode.HasValue?"     /     EXIT "+r.remoteExitCode:""),12);detail.Foreground=Muted;detail.FontFamily=new FontFamily("Consolas");header.Children.Add(detail);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        root.Children.Add(new TextBox{Text=String.IsNullOrEmpty(r.stdout)?"没有附加输出。":r.stdout,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Consolas"),FontSize=13,Padding=new Thickness(18),VerticalContentAlignment=VerticalAlignment.Top});w.Content=root;w.ShowDialog();}
}
public sealed class MainWindow:Window {
    readonly Store store;readonly ListView list=new ListView();readonly TextBox search=new TextBox();readonly ComboBox groups=new ComboBox();readonly TextBlock status=Ui.Text("");readonly Forms.NotifyIcon tray;
    readonly TextBlock connectionCount=Ui.Text("00",30);
    Editor editor;bool exit,busy;List<Profile> profiles=new List<Profile>();
    public MainWindow():this(new Store()){}
    public MainWindow(Store store){
        if(store==null)throw new ArgumentNullException("store");this.store=store;
        Ui.Window(this,"服务器管理",1180,800);status.ToolTip="配置目录："+store.Root;System.Windows.Automation.AutomationProperties.SetHelpText(status,"配置目录："+store.Root);
        var root=new DockPanel{Margin=new Thickness(32,26,32,24)};
        var header=new DockPanel{Margin=new Thickness(0,0,0,26)};var add=Ui.Button("＋  新增服务器",(s,e)=>OpenEditor(null),true);add.Margin=new Thickness(0,10,0,0);add.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(add,Dock.Right);header.Children.Add(add);var brand=new StackPanel();brand.Children.Add(new TextBlock{Text="SERVER MANAGER  /  WINDOWS",FontFamily=new FontFamily("Consolas"),FontSize=11,Foreground=Ui.Cyan,Margin=new Thickness(0,0,0,9)});brand.Children.Add(new TextBlock{Text="服务器控制台",FontSize=30,FontWeight=FontWeights.SemiBold,Foreground=Ui.Ink});brand.Children.Add(new TextBlock{Text="连接你的基础设施，让凭据留在本机。",Margin=new Thickness(0,8,0,0),Foreground=Ui.Muted,FontSize=13});header.Children.Add(brand);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var metrics=new Grid{Margin=new Thickness(0,0,0,24)};for(int i=0;i<3;i++)metrics.ColumnDefinitions.Add(new ColumnDefinition());connectionCount.Foreground=Ui.Cyan;connectionCount.FontFamily=new FontFamily("Consolas");AddMetric(metrics,0,"已保存连接",connectionCount,"SERVER PROFILES");AddMetric(metrics,1,"访问协议",Ui.Text("SSH  /  SFTP",23),"Linux  ·  主机密钥严格校验");AddMetric(metrics,2,"凭据保护",Ui.Text("Windows 凭据管理器",20),"仅在本机安全输入与使用");DockPanel.SetDock(metrics,Dock.Top);root.Children.Add(metrics);
        var section=new DockPanel{Margin=new Thickness(0,0,0,16)};var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};actions.Children.Add(Ui.Button("编辑连接",(s,e)=>WithSelected(p=>OpenEditor(p.Alias))));actions.Children.Add(Ui.Button("刷新",(s,e)=>Refresh()));actions.Children.Add(Ui.Button("导入 SSH",(s,e)=>Import()));var del=Ui.Button("删除",(s,e)=>Remove());del.Foreground=Ui.ColorBrush("#DA9DA0");del.Margin=new Thickness(0);actions.Children.Add(del);DockPanel.SetDock(actions,Dock.Right);section.Children.Add(actions);var listTitle=Ui.Text("服务器列表",18);listTitle.FontWeight=FontWeights.SemiBold;listTitle.VerticalAlignment=VerticalAlignment.Center;section.Children.Add(listTitle);DockPanel.SetDock(section,Dock.Top);root.Children.Add(section);
        var filters=new Grid{Margin=new Thickness(0,0,0,18)};filters.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(205)});filters.ColumnDefinitions.Add(new ColumnDefinition());groups.Margin=new Thickness(0,0,12,0);groups.MinHeight=42;groups.SelectionChanged+=(s,e)=>Filter();filters.Children.Add(groups);search.Padding=new Thickness(14,10,14,10);search.Tag="搜索别名、主机地址或账号…";search.ToolTip="搜索别名、主机地址或账号";System.Windows.Automation.AutomationProperties.SetName(search,"搜索服务器");Grid.SetColumn(search,1);search.TextChanged+=(s,e)=>Filter();filters.Children.Add(search);DockPanel.SetDock(filters,Dock.Top);root.Children.Add(filters);
        var footer=new StackPanel{Margin=new Thickness(0,20,0,0)};var tools=new WrapPanel();tools.Children.Add(Ui.Button("›_  SSH 终端",(s,e)=>Terminal(false),true));tools.Children.Add(Ui.Button("⇄  SFTP 终端",(s,e)=>Terminal(true)));tools.Children.Add(Ui.Button("测试 SSH",async(s,e)=>await Check(false)));tools.Children.Add(Ui.Button("测试 sudo",async(s,e)=>await Check(true)));footer.Children.Add(tools);status.Margin=new Thickness(0,16,0,6);status.Foreground=Ui.Muted;status.FontSize=12;footer.Children.Add(status);footer.Children.Add(new TextBlock{Text="●  本机安全存储     /     关闭窗口后驻留托盘",Foreground=Ui.ColorBrush("#537390"),FontSize=11});DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        list.Background=Brushes.Transparent;list.BorderThickness=new Thickness(0);list.Padding=new Thickness(0);ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        list.ItemTemplate=(DataTemplate)XamlReader.Parse(@"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='1.1*'/><ColumnDefinition Width='1.7*'/><ColumnDefinition Width='.8*'/><ColumnDefinition Width='1.2*'/></Grid.ColumnDefinitions><StackPanel VerticalAlignment='Center'><TextBlock Text='{Binding Alias}' Foreground='#DFEEFA' FontWeight='SemiBold' FontSize='15'/><TextBlock Text='LINUX  /  SSH' Foreground='#537F9F' FontFamily='Consolas' FontSize='10' Margin='0,5,0,0'/></StackPanel><TextBlock Grid.Column='1' Text='{Binding Endpoint}' Foreground='#9EB9CF' FontFamily='Consolas' FontSize='14' VerticalAlignment='Center'/><Border Grid.Column='2' Background='#16344B' CornerRadius='5' Padding='9,5' HorizontalAlignment='Left' VerticalAlignment='Center'><TextBlock Text='{Binding GroupLabel}' Foreground='#81C8E5' FontSize='12'/></Border><StackPanel Grid.Column='3' Orientation='Horizontal' VerticalAlignment='Center'><TextBlock Text='●' Foreground='#4CABC3' FontSize='8' VerticalAlignment='Center' Margin='0,0,9,0'/><TextBlock Text='{Binding AuthLabel}' Foreground='#A1BBD0' FontSize='12' VerticalAlignment='Center'/></StackPanel></Grid></DataTemplate>");
        list.MouseDoubleClick+=(s,e)=>WithSelected(p=>OpenEditor(p.Alias));var table=new DockPanel();var headings=new Grid{Margin=new Thickness(16,0,16,12)};foreach(var width in new[]{1.1,1.7,.8,1.2})headings.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(width,GridUnitType.Star)});int col=0;foreach(var text in new[]{"连接名称","服务器地址","分组","认证方式"}){var label=Ui.Text(text,11);label.Foreground=Ui.Muted;Grid.SetColumn(label,col++);headings.Children.Add(label);}DockPanel.SetDock(headings,Dock.Top);table.Children.Add(headings);table.Children.Add(list);var frame=Ui.Card(table);frame.Padding=new Thickness(14,17,14,14);root.Children.Add(frame);Content=root;
        tray=new Forms.NotifyIcon{Text="服务器管理",Icon=new System.Drawing.Icon(Path.Combine(Engine.Bin,"AppIcon.ico")),Visible=true};tray.DoubleClick+=(s,e)=>Dispatcher.Invoke((Action)Wake);
        Closing+=(s,e)=>{if(!exit){e.Cancel=true;Hide();}};Closed+=(s,e)=>tray.Dispose();Loaded+=(s,e)=>Refresh();
    }
    void AddMetric(Grid grid,int column,string label,TextBlock value,string note){var content=new StackPanel();content.Children.Add(new TextBlock{Text=label,Foreground=Ui.Muted,FontSize=12});value.Margin=new Thickness(0,9,0,7);content.Children.Add(value);content.Children.Add(new TextBlock{Text=note,Foreground=Ui.ColorBrush("#557A99"),FontSize=11,FontFamily=new FontFamily("Microsoft YaHei UI")});var card=Ui.Card(content);card.Margin=new Thickness(column==0?0:8,0,column==2?0:8,0);Grid.SetColumn(card,column);grid.Children.Add(card);}
    internal void Wake(){if(!IsVisible)Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;if(editor==null&&!busy){var selected=list.SelectedItem as Profile;Refresh(selected==null?null:selected.Alias);}Activate();FocusTop();}
    void FocusTop(){var child=OwnedWindows.Cast<Window>().LastOrDefault(w=>w.IsVisible);if(child!=null){child.Activate();var top=child.OwnedWindows.Cast<Window>().LastOrDefault(w=>w.IsVisible);if(top!=null)top.Activate();}}
    internal void ActivateRequest(string command){if(command=="quit"){Quit();return;}Wake();if(command.StartsWith("edit:")){var alias=command.Substring(5);OpenEditor(alias.Length==0?null:alias);}}
    internal bool CanExit {get{return editor==null&&!busy;}}
    internal void Quit(){if(!CanExit){Wake();FocusTop();Ui.Notice(editor??(Window)this,"当前操作尚未完成","请先完成或取消当前编辑 / 验证，再退出工具。");return;}exit=true;Close();}
    void WithSelected(Action<Profile> a){if(editor!=null){FocusTop();return;}var p=list.SelectedItem as Profile;if(p!=null)a(p);else status.Text="请先选择一台服务器。";}
    internal void OpenEditor(string alias){if(editor!=null||busy){FocusTop();return;}try{Profile p=alias==null?null:store.Read(alias);editor=new Editor(store,p){Owner=this,ShowInTaskbar=false};editor.ShowDialog();if(editor.SavedAlias!=null){search.Clear();Refresh(editor.SavedAlias,true);}}catch(Exception ex){Ui.Error(this,ex);}finally{editor=null;}}
    void Refresh(string selected=null,bool clearGroup=false){try{profiles=store.List();connectionCount.Text=profiles.Count.ToString("D2");string previous=groups.SelectedItem as string;groups.ItemsSource=new[]{"全部分组"}.Concat(profiles.Select(p=>p.GroupLabel).Distinct()).ToList();groups.SelectedItem=!clearGroup&&previous!=null&&groups.Items.Contains(previous)?previous:"全部分组";Filter();if(selected!=null)list.SelectedItem=profiles.FirstOrDefault(p=>p.Alias==selected);RebuildTray();status.Text="共 "+profiles.Count+" 台服务器  ·  选择连接以打开终端或执行测试";}catch(Exception ex){Ui.Error(this,ex);}}
    void Filter(){var q=search.Text.Trim();var group=groups.SelectedItem as string;list.ItemsSource=profiles.Where(p=>(group==null||group=="全部分组"||p.GroupLabel==group)&&(q.Length==0||(p.Alias+" "+p.Host+" "+p.User).IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0)).ToList();}
    void RebuildTray(){var menu=new Forms.ContextMenuStrip();menu.Items.Add("打开服务器管理",null,(s,e)=>Dispatcher.Invoke((Action)Wake));menu.Items.Add("新增服务器",null,(s,e)=>Dispatcher.Invoke((Action)(()=>{Wake();OpenEditor(null);})));
        foreach(var group in profiles.GroupBy(p=>p.GroupLabel)){var g=new Forms.ToolStripMenuItem(group.Key);foreach(var p in group){var item=new Forms.ToolStripMenuItem(p.Alias+" · "+p.Endpoint);item.DropDownItems.Add("打开 SSH 终端",null,(s,e)=>Dispatcher.Invoke((Action)(()=>TrayTerminal(p,false))));item.DropDownItems.Add("打开 SFTP 终端",null,(s,e)=>Dispatcher.Invoke((Action)(()=>TrayTerminal(p,true))));item.DropDownItems.Add("编辑连接",null,(s,e)=>Dispatcher.Invoke((Action)(()=>{Wake();OpenEditor(p.Alias);})));g.DropDownItems.Add(item);}menu.Items.Add(g);}
        menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("退出工具",null,(s,e)=>Dispatcher.Invoke((Action)Quit));var old=tray.ContextMenuStrip;tray.ContextMenuStrip=menu;if(old!=null)old.Dispose();}
    void TrayTerminal(Profile p,bool sftp){if(editor!=null||busy){Wake();FocusTop();return;}LaunchTerminal(p,sftp);}
    void Terminal(bool sftp){WithSelected(p=>LaunchTerminal(p,sftp));}
    void LaunchTerminal(Profile p,bool sftp){try{Process.Start(new ProcessStartInfo(Path.Combine(Engine.Bin,"server-access.exe"),Engine.Args(new[]{sftp?"sftp":"ssh",p.Alias})){UseShellExecute=true});}catch(Exception ex){Ui.Error(this,ex);}}
    async Task Check(bool sudo){var p=list.SelectedItem as Profile;if(p==null||busy||editor!=null)return;busy=true;status.Text="正在测试 "+p.Alias+"…";try{var r=await Task.Run(()=>store.Named(p.Alias,sudo?"sudo-check":"status",30));Ui.Result(this,r);status.Text=r.message;}catch(Exception ex){Ui.Error(this,ex);}finally{busy=false;}}
    void Remove(){WithSelected(p=>{if(!Ui.Confirm(this,"删除本机连接","删除“"+p.Alias+"”及本工具为它保存的凭据？\n\n已有 OpenSSH 配置和外部凭据条目会保留。远端不受影响。","确认删除"))return;try{store.Delete(p);Refresh();}catch(Exception ex){Ui.Error(this,ex);}});}
    void Import(){try{int n=store.ImportOpenSsh();Refresh();status.Text="已导入 "+n+" 个已有 SSH 别名。仅导入具有已知主机密钥的明确别名；现有配置未覆盖。";}catch(Exception ex){Ui.Error(this,ex);}}
}
public sealed class Editor:Window {
    readonly Store store;readonly Profile original;readonly TextBox alias=new TextBox(),host=new TextBox(),user=new TextBox(),port=new TextBox(),keyPath=new TextBox();readonly ComboBox group=new ComboBox(),auth=new ComboBox();
    readonly PasswordBox login=new PasswordBox(),phrase=new PasswordBox(),sudo=new PasswordBox();readonly CheckBox same=new CheckBox{Content="sudo 与 SSH 登录密码一致"},removeSudo=new CheckBox{Content="删除已保存的单独 sudo 凭据"};
    readonly TextBlock error=Ui.Text("");readonly StackPanel fields=new StackPanel();readonly Button save;bool busy,guard,manualSame;string confirmed,confirmedHost;int confirmedPort;
    readonly Dictionary<FrameworkElement,FrameworkElement> fieldBlocks=new Dictionary<FrameworkElement,FrameworkElement>();FrameworkElement keyBlock;
    public string SavedAlias {get;private set;}
    public Editor(Store store,Profile original){this.store=store;this.original=original;Ui.Window(this,original==null?"新增服务器":"编辑服务器 · "+original.Alias,650,820);WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(28,22,28,22)};var header=new StackPanel{Margin=new Thickness(0,0,0,20)};header.Children.Add(new TextBlock{Text="CONNECTION PROFILE",FontFamily=new FontFamily("Consolas"),Foreground=Ui.Cyan,FontSize=11,Margin=new Thickness(0,0,0,9)});header.Children.Add(Ui.Text(original==null?"添加服务器":"编辑连接",26));header.Children.Add(new TextBlock{Text=original==null?"填写连接信息，在验证成功后安全保存。":"留空保留原密码；更换账号或认证方式时重新填写凭据。",TextWrapping=TextWrapping.Wrap,Foreground=Ui.Muted,FontSize=12,Margin=new Thickness(0,8,0,0)});DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var footer=new StackPanel{Margin=new Thickness(0,18,0,0)};error.Foreground=Ui.ColorBrush("#FFB498");error.FontSize=12;error.Margin=new Thickness(0,0,0,14);footer.Children.Add(error);var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};buttons.Children.Add(Ui.Button("取消",(s,e)=>{if(!busy)Close();}));save=Ui.Button("验证并保存",async(s,e)=>await Save(),true);buttons.Children.Add(save);footer.Children.Add(buttons);DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=fields,Padding=new Thickness(0,0,8,0)};var form=Ui.Card(scroll);form.Padding=new Thickness(20,18,12,14);root.Children.Add(form);Content=root;
        alias.IsReadOnly=original!=null;group.IsEditable=true;group.ItemsSource=store.List().Select(p=>p.Group).Where(g=>!String.IsNullOrEmpty(g)).Distinct().ToList();AddPair("别名（英文 / 数字 / _ / -）",alias,"分组（支持中文）",group);AddPair("服务器地址",host,"端口",port,2.8);
        auth.Items.Add("私钥 / SSH Agent");auth.Items.Add("SSH 登录密码");AddPair("登录账号",user,"认证方式",auth);
        var keyRow=new DockPanel();var browse=Ui.Button("选择文件",(s,e)=>Browse());browse.Padding=new Thickness(12,7,12,7);DockPanel.SetDock(browse,Dock.Right);keyRow.Children.Add(browse);var clear=Ui.Button("清除",(s,e)=>keyPath.Clear());clear.Padding=new Thickness(12,7,12,7);DockPanel.SetDock(clear,Dock.Right);keyRow.Children.Add(clear);keyPath.IsReadOnly=true;keyPath.Padding=new Thickness(9,6,9,6);keyRow.Children.Add(keyPath);Add("私钥文件（可复用 SSH Agent）",keyRow);keyBlock=fieldBlocks[keyRow];Add("SSH 登录密码",login);Add("私钥解锁口令（不等于 sudo 密码）",phrase);
        same.Margin=new Thickness(0,5,0,16);fields.Children.Add(same);Add("单独 sudo 密码（可选）",sudo);removeSudo.Margin=new Thickness(0,2,0,14);fields.Children.Add(removeSudo);removeSudo.Visibility=original!=null&&original.SudoRef!=null?Visibility.Visible:Visibility.Collapsed;
        fields.Children.Add(Ui.Button("重新核对主机指纹",async(s,e)=>await Recheck()));fields.Children.Add(new TextBlock{Text="请通过可信来源独立核对指纹。密码仅用于本机认证，不会写入普通配置。",TextWrapping=TextWrapping.Wrap,Foreground=Ui.Muted,FontSize=11,Margin=new Thickness(0,14,0,8)});
        guard=true;alias.Text=original==null?"":original.Alias;group.Text=original==null?"":original.Group;host.Text=original==null?"":original.Host;user.Text=original==null?"":original.User;port.Text=(original==null?22:original.Port).ToString();keyPath.Text=original==null?"":original.KeyPath;auth.SelectedIndex=original==null||original.Auth=="key"?0:1;same.IsChecked=original!=null&&original.SudoSame;manualSame=original!=null;confirmed=original==null?null:original.HostKeys;confirmedHost=original==null?null:original.Host;confirmedPort=original==null?0:original.Port;guard=false;
        auth.SelectionChanged+=(s,e)=>{if(!guard){guard=true;same.IsChecked=auth.SelectedIndex==1&&!manualSame&&login.Password.Length>0;guard=false;UpdateAuth();}};
        login.PasswordChanged+=(s,e)=>{if(guard)return;bool available=auth.SelectedIndex==1&&removeSudo.IsChecked!=true&&(login.Password.Length>0||(original!=null&&original.Auth=="password"&&original.LoginRef!=null));guard=true;if(!manualSame)same.IsChecked=available;else if(!available)same.IsChecked=false;guard=false;UpdateAuth();};
        same.Checked+=(s,e)=>{if(!guard)manualSame=true;UpdateAuth();};same.Unchecked+=(s,e)=>{if(!guard)manualSame=true;UpdateAuth();};removeSudo.Checked+=(s,e)=>{guard=true;same.IsChecked=false;guard=false;UpdateAuth();};removeSudo.Unchecked+=(s,e)=>UpdateAuth();UpdateAuth();
        Closing+=(s,e)=>{if(busy)e.Cancel=true;};Closed+=(s,e)=>{login.Clear();phrase.Clear();sudo.Clear();};
    }
    void Add(string title,Control control){AddElement(title,control);control.Padding=new Thickness(9,6,9,6);}
    void Add(string title,DockPanel control){AddElement(title,control);}
    FrameworkElement MakeField(string title,FrameworkElement control){var block=new StackPanel{Margin=new Thickness(0,0,0,17)};block.Children.Add(new TextBlock{Text=title,Foreground=Ui.Muted,FontSize=12,Margin=new Thickness(0,0,0,7)});control.MinHeight=39;System.Windows.Automation.AutomationProperties.SetName(control,title);block.Children.Add(control);fieldBlocks[control]=block;return block;}
    void AddElement(string title,FrameworkElement control){fields.Children.Add(MakeField(title,control));}
    void AddPair(string leftTitle,Control left,string rightTitle,Control right,double ratio=1){var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(ratio,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});left.Padding=right.Padding=new Thickness(9,6,9,6);var l=MakeField(leftTitle,left);l.Margin=new Thickness(0,0,12,17);row.Children.Add(l);var r=MakeField(rightTitle,right);Grid.SetColumn(r,1);row.Children.Add(r);fields.Children.Add(row);}
    void UpdateAuth(){bool password=auth.SelectedIndex==1;login.IsEnabled=password;phrase.IsEnabled=!password;keyPath.IsEnabled=!password;bool existing=original!=null&&original.Auth=="password"&&original.LoginRef!=null&&host.Text==original.Host&&user.Text==original.User;
        same.IsEnabled=password&&!removeSudo.IsChecked.GetValueOrDefault()&&(login.Password.Length>0||existing);
        if(!password&&same.IsChecked==true){guard=true;same.IsChecked=false;guard=false;}sudo.IsEnabled=same.IsChecked!=true&&removeSudo.IsChecked!=true;removeSudo.IsEnabled=same.IsChecked!=true;
        if(keyBlock!=null)keyBlock.Visibility=password?Visibility.Collapsed:Visibility.Visible;fieldBlocks[login].Visibility=password?Visibility.Visible:Visibility.Collapsed;fieldBlocks[phrase].Visibility=password?Visibility.Collapsed:Visibility.Visible;}
    void Browse(){if(busy)return;var dlg=new OpenFileDialog{Title="选择 SSH 私钥文件",CheckFileExists=true,Multiselect=false,Filter="所有文件|*.*"};if(dlg.ShowDialog(this)==true)keyPath.Text=dlg.FileName;}
    Profile Candidate(){int n;if(!Int32.TryParse(port.Text.Trim(),out n))throw new AccessException("INVALID_PORT","端口须为整数。");var p=new Profile{Alias=alias.Text.Trim(),Group=group.Text.Trim(),Host=host.Text.Trim(),User=user.Text.Trim(),Port=n,Auth=auth.SelectedIndex==0?"key":"password",KeyPath=auth.SelectedIndex==0?keyPath.Text:"",SudoSame=same.IsChecked==true,Operations=original==null?new Dictionary<string,Operation>():original.Operations};Store.Validate(p);return p;}
    async Task<string> Confirm(Profile p){error.Text="正在扫描公开的主机密钥…";var keys=await Task.Run(()=>Engine.Scan(p));
        var answer=Ui.Confirm(this,"独立核对主机指纹","请通过服务器控制台或管理员提供的可信渠道，独立核对以下指纹：\n\n"+p.Endpoint+"\n\n"+Engine.Fingerprints(keys)+"\n\n只有所有指纹均可信时才继续。扫描结果本身不证明服务器身份。","我已独立核对，继续");
        if(!answer)throw new AccessException("HOST_UNCONFIRMED","尚未确认主机指纹，配置未保存。","host-key");confirmedHost=p.Host;confirmedPort=p.Port;return keys;}
    async Task Recheck(){if(busy)return;busy=true;save.IsEnabled=false;fields.IsEnabled=false;try{var p=Candidate();confirmed=await Confirm(p);error.Text="主机指纹已确认；点击“验证并保存”完成连接验证。";}catch(AccessException ex){error.Text=ex.Message;}catch{error.Text="未能取得主机密钥，请检查网络。";}finally{busy=false;save.IsEnabled=true;fields.IsEnabled=true;}}
    async Task Save(){if(busy)return;busy=true;save.IsEnabled=false;fields.IsEnabled=false;try{
        var p=Candidate();if(confirmed==null||p.Host!=confirmedHost||p.Port!=confirmedPort)confirmed=await Confirm(p);p.HostKeys=confirmed;
        string loginValue=login.Password,phraseValue=phrase.Password,sudoValue=sudo.Password;bool remove=removeSudo.IsChecked==true;
        await Task.Run(()=>store.Save(p,original,loginValue,phraseValue,sudoValue,remove,text=>Dispatcher.Invoke((Action)(()=>error.Text=text))));loginValue=phraseValue=sudoValue=null;
        SavedAlias=p.Alias;busy=false;Close();
    }catch(AccessException ex){error.Text="["+ex.Stage+" / "+ex.Code+"] "+ex.Message;}catch{error.Text="本机保存失败。原配置尚未被替换；请检查文件和凭据库权限。";}finally{busy=false;save.IsEnabled=true;fields.IsEnabled=true;}}
}
internal static class DesktopProgram {
    [STAThread] public static int Main(string[] args){
        string sid=WindowsIdentity.GetCurrent().User.Value;bool first;
        using(var mutex=new Mutex(true,"Local\\ServerManager.Desktop."+sid,out first)){
            var channel="ServerManager.Desktop."+Store.Digest(sid);var request=args.Length>0&&args[0]=="--quit"?"quit":args.Length>1&&args[0]=="--configure"?"edit:"+args[1]:args.Length>0&&args[0]=="--configure"?"edit:":"show";
            if(!first){try{using(var client=new NamedPipeClientStream(".",channel,PipeDirection.Out)){client.Connect(5000);using(var writer=new BinaryWriter(client))writer.Write(request);}return 0;}catch{return 1;}}
            if(request=="quit")return 0;
            var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};MainWindow main;
            try{main=new MainWindow();}catch(Exception){MessageBox.Show("无法打开服务器管理，请检查安装文件和本机配置。","服务器管理");return 1;}
            bool stopped=false;NamedPipeServerStream active=null;
            var listener=Task.Run(()=>{while(!stopped){try{var acl=new PipeSecurity();acl.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));using(var pipe=new NamedPipeServerStream(channel,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,acl)){active=pipe;pipe.WaitForConnection();using(var reader=new BinaryReader(pipe)){string cmd=reader.ReadString();if(cmd.Length<=128)app.Dispatcher.BeginInvoke((Action)(()=>main.ActivateRequest(cmd)));}}}catch(IOException){}catch(ObjectDisposedException){}finally{active=null;}}});
            main.Loaded+=(s,e)=>{if(request.StartsWith("edit:"))app.Dispatcher.BeginInvoke((Action)(()=>main.ActivateRequest(request)));};
            app.Run(main);stopped=true;var pending=active;if(pending!=null)pending.Dispose();try{listener.Wait(1000);}catch{}mutex.ReleaseMutex();return 0;
        }
    }
}
}
