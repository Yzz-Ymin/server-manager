using System;
using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
namespace ServerManager {
internal static class UiTests {
    static int count;
    static void Flush(){System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{}));}
    static void Quit(MainWindow window){typeof(MainWindow).GetMethod("Quit",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);}
    static T Field<T>(object instance,string name){return (T)instance.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(instance);}
    static void Check(bool yes,string name){if(!yes)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
    [STAThread] public static int Main(string[] args){try{
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var store=new Store(args[0],"ServerManagerTest/UI/");var editor=new Editor(store,null);
        var auth=Field<ComboBox>(editor,"auth");var login=Field<PasswordBox>(editor,"login");var phrase=Field<PasswordBox>(editor,"phrase");var sudo=Field<PasswordBox>(editor,"sudo");var same=Field<CheckBox>(editor,"same");
        Check(!same.IsEnabled&&same.IsChecked!=true,"new key form does not enable same-password sudo");
        auth.SelectedIndex=1;Check(!same.IsEnabled,"empty login does not enable same-password sudo");
        login.Password="synthetic-ui-only";Check(same.IsEnabled&&same.IsChecked==true&&!sudo.IsEnabled,"first password enables same-password and disables separate sudo");
        same.IsChecked=false;login.Password="synthetic-ui-edited";Check(same.IsChecked==false&&sudo.IsEnabled,"manual same-password opt-out stays off while typing");
        auth.SelectedIndex=0;phrase.Password="synthetic-key-phrase";Check(same.IsChecked==false&&!same.IsEnabled&&phrase.IsEnabled&&!login.IsEnabled,"private-key mode never maps passphrase to sudo");editor.Close();
        var original=new Profile{Alias="ui-edit",Host="127.0.0.1",User="tester",Auth="password",LoginRef="ServerManager/ui-edit/login",SudoRef="ServerManager/ui-edit/sudo",SudoSame=true};
        editor=new Editor(store,original);same=Field<CheckBox>(editor,"same");login=Field<PasswordBox>(editor,"login");sudo=Field<PasswordBox>(editor,"sudo");
        Check(same.IsChecked==true&&same.IsEnabled&&!sudo.IsEnabled,"editing preserves saved same-password choice");Check(login.Password.Length==0&&sudo.Password.Length==0,"editing never preloads stored secrets into controls");
        login.Password="synthetic-updated-login";Check(same.IsChecked==true,"updating login preserves enabled same-password mode");
        Check(Field<TextBox>(editor,"alias").IsReadOnly,"alias cannot be renamed in-place");editor.Close();
        editor=new Editor(store,null);var content=(FrameworkElement)editor.Content;content.Measure(new Size(630,780));content.Arrange(new Rect(0,0,630,780));content.UpdateLayout();
        var bitmap=new RenderTargetBitmap(630,780,96,96,PixelFormats.Pbgra32);bitmap.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(Path.GetDirectoryName(args[0]),"form-preview.png")))encoder.Save(output);editor.Close();
        var persisted=new Store(Path.Combine(args[0],"reopen-"+Guid.NewGuid().ToString("N")),"ServerManagerTest/UI/");Directory.CreateDirectory(Path.Combine(persisted.Root,"profiles"));
        for(int i=1;i<=3;i++){var p=new Profile{Alias="saved-"+i,Host="127.0.0.1",User="tester",Auth="password",Group="Synthetic"};Store.AtomicWrite(persisted.ProfilePath(p.Alias),Store.Json(p));}
        var main=new MainWindow(persisted);main.Show();Flush();
        Check(Field<ListView>(main,"list").Items.Count==3&&Field<TextBlock>(main,"connectionCount").Text=="03","startup displays persisted connections after window load");
        main.Close();Check(!main.IsVisible,"closing the main window hides it to the tray");
        var added=new Profile{Alias="saved-4",Host="127.0.0.1",User="tester",Auth="password"};Store.AtomicWrite(persisted.ProfilePath(added.Alias),Store.Json(added));
        typeof(MainWindow).GetMethod("Wake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(main,null);Flush();
        Check(Field<ListView>(main,"list").Items.Count==4&&Field<TextBlock>(main,"connectionCount").Text=="04","reopening from tray reloads connections changed on disk");Quit(main);
        main=new MainWindow(new Store(persisted.Root,"ServerManagerTest/UI/"));main.Show();Flush();
        Check(Field<ListView>(main,"list").Items.Count==4,"full exit and fresh window preserve saved connections");Quit(main);
        var absent=new Store(Path.Combine(args[0],"absent-"+Guid.NewGuid().ToString("N")),"ServerManagerTest/UI/");Check(absent.List().Count==0,"new installation has an empty connection list");
        Directory.CreateDirectory(absent.Root);File.WriteAllText(Path.Combine(absent.Root,"profiles"),"synthetic path obstruction");bool reported=false;try{absent.List();}catch(AccessException ex){reported=ex.Code=="CONFIG_READ_FAILED";}
        Check(reported,"invalid configuration directory reports an error instead of an empty list");
        var legacyB=new Store(Path.Combine(args[0],"legacy-"+Guid.NewGuid().ToString("N")),"ServerManagerTest/UI/");Directory.CreateDirectory(Path.Combine(legacyB.Root,"profiles"));
        string originalText=File.ReadAllText(persisted.ProfilePath("saved-1"));Store.AtomicWrite(legacyB.ProfilePath("saved-1"),originalText);
        var fifth=new Profile{Alias="saved-5",Host="127.0.0.1",User="tester",Auth="password"};Store.AtomicWrite(legacyB.ProfilePath(fifth.Alias),Store.Json(fifth));
        var migrated=new Store(Path.Combine(args[0],"shared-"+Guid.NewGuid().ToString("N")),"ServerManagerTest/UI/");migrated.MigrateLegacy(new[]{persisted.Root,legacyB.Root});
        Check(migrated.List().Count==5,"migration merges normal and isolated legacy data without duplicate aliases");
        Check(File.ReadAllText(migrated.ProfilePath("saved-1"))==originalText&&persisted.List().Count==4&&legacyB.List().Count==2,"migration preserves profile bytes and keeps both legacy sources");
        File.Delete(migrated.ProfilePath("saved-1"));migrated.MigrateLegacy(new[]{persisted.Root,legacyB.Root});
        Check(migrated.List().Count==4&&!File.Exists(migrated.ProfilePath("saved-1")),"completed migration never resurrects deleted connections");
        var changed=legacyB.Read("saved-1");changed.Host="127.0.0.2";Store.AtomicWrite(legacyB.ProfilePath(changed.Alias),Store.Json(changed));
        var conflict=new Store(Path.Combine(args[0],"conflict-"+Guid.NewGuid().ToString("N")),"ServerManagerTest/UI/");bool collision=false;try{conflict.MigrateLegacy(new[]{persisted.Root,legacyB.Root});}catch(AccessException ex){collision=ex.Code=="CONFIG_MIGRATION_CONFLICT";}
        Check(collision&&conflict.List().Count==0&&!File.Exists(Path.Combine(conflict.Root,"migration.json")),"conflicting legacy profiles abort before copying or marking migration complete");
        var resumed=new Store(Path.Combine(args[0],"resume-"+Guid.NewGuid().ToString("N")),"ServerManagerTest/UI/");Directory.CreateDirectory(Path.Combine(resumed.Root,"profiles"));Store.AtomicWrite(resumed.ProfilePath("saved-1"),originalText);resumed.MigrateLegacy(new[]{persisted.Root});
        Check(resumed.List().Count==4,"interrupted migration resumes without replacing already copied profiles");
        main=new MainWindow(new Store(resumed.Root,"ServerManagerTest/UI/"));main.Show();Flush();Check(Field<ListView>(main,"list").Items.Count==4,"fresh window reads the shared migrated store");Quit(main);app.Shutdown();
        Console.WriteLine("PASSED "+count+" UI state checks");return 0;
    }catch(Exception ex){Console.WriteLine("FAIL "+ex.Message);return 1;}}
}
}
