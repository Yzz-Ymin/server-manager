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
    static T Field<T>(object instance,string name){return (T)instance.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(instance);}
    static void Check(bool yes,string name){if(!yes)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
    [STAThread] public static int Main(string[] args){try{
        var store=new Store(args[0],"ServerManagerTest/UI/");var editor=new Editor(store,null);
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
        Console.WriteLine("PASSED "+count+" UI state checks");return 0;
    }catch(Exception ex){Console.WriteLine("FAIL "+ex.Message);return 1;}}
}
}
