using System.Windows;
using System.Windows.Input;

namespace MetricBot;

public partial class PasswordWindow : Window
{
    public enum PasswordMode { Unlock, Create, Change, Remove }

    private readonly PasswordMode _mode;

    public PasswordWindow(PasswordMode mode)
    {
        InitializeComponent();
        _mode = mode;
        ConfigureMode();
    }

    private void ConfigureMode()
    {
        switch (_mode)
        {
            case PasswordMode.Create:
                TxtTitle.Text = "УСТАНОВКА ПАРОЛЯ";
                TxtHint.Text = "Придумайте пароль. Если вы его забудете, защиту можно сбросить удалением security.json из %LocalAppData%\\MetricBot.";
                PwdCurrent.Visibility = Visibility.Collapsed;
                PwdNew.Visibility = Visibility.Visible;
                PwdConfirm.Visibility = Visibility.Visible;
                BtnConfirm.Content = "Установить";
                break;
            case PasswordMode.Change:
                TxtTitle.Text = "СМЕНА ПАРОЛЯ";
                TxtHint.Text = "Введите текущий пароль, затем новый пароль дважды.";
                PwdNew.Visibility = Visibility.Visible;
                PwdConfirm.Visibility = Visibility.Visible;
                BtnConfirm.Content = "Сменить";
                break;
            case PasswordMode.Remove:
                TxtTitle.Text = "ОТКЛЮЧЕНИЕ ЗАЩИТЫ";
                TxtHint.Text = "Введите текущий пароль для отключения защиты.";
                BtnConfirm.Content = "Отключить";
                break;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        (_mode == PasswordMode.Create ? PwdNew : PwdCurrent).Focus();
    }

    private void Password_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Submit();
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e) => Submit();

    private void Submit()
    {
        TxtError.Visibility = Visibility.Collapsed;

        if (_mode != PasswordMode.Create && !PasswordService.Verify(PwdCurrent.Password))
        {
            ShowError("Неверный пароль.");
            PwdCurrent.SelectAll();
            PwdCurrent.Focus();
            return;
        }

        if (_mode is PasswordMode.Create or PasswordMode.Change)
        {
            if (PwdNew.Password.Length < 4)
            {
                ShowError("Пароль должен содержать не менее 4 символов.");
                PwdNew.Focus();
                return;
            }

            if (PwdNew.Password != PwdConfirm.Password)
            {
                ShowError("Введённые пароли не совпадают.");
                PwdConfirm.SelectAll();
                PwdConfirm.Focus();
                return;
            }

            try { PasswordService.Set(PwdNew.Password); }
            catch (Exception ex) { ShowError($"Не удалось сохранить пароль: {ex.Message}"); return; }
        }
        else if (_mode == PasswordMode.Remove)
        {
            try { PasswordService.Remove(); }
            catch (Exception ex) { ShowError($"Не удалось удалить пароль: {ex.Message}"); return; }
        }

        DialogResult = true;
    }

    private void ShowError(string message)
    {
        TxtError.Text = message;
        TxtError.Visibility = Visibility.Visible;
    }
}
