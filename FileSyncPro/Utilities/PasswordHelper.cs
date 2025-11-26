#nullable enable
using System.Windows;
using System.Windows.Controls;

namespace FileSyncPro.Utilities
{
    public static class PasswordHelper
    {
        public static readonly DependencyProperty PasswordProperty =
            DependencyProperty.RegisterAttached("Password",
            typeof(string), typeof(PasswordHelper),
            new FrameworkPropertyMetadata(string.Empty, OnPasswordPropertyChanged));

        public static readonly DependencyProperty AttachProperty =
            DependencyProperty.RegisterAttached("Attach",
            typeof(bool), typeof(PasswordHelper), 
            new PropertyMetadata(false, Attach));

        private static readonly DependencyProperty IsUpdatingProperty =
           DependencyProperty.RegisterAttached("IsUpdating", 
           typeof(bool), typeof(PasswordHelper));

        public static void SetAttach(DependencyObject dp, bool value)
        {
            dp.SetValue(AttachProperty, value);
        }

        public static bool GetAttach(DependencyObject dp)
        {
            return (bool)dp.GetValue(AttachProperty);
        }

        public static string GetPassword(DependencyObject dp)
        {
            return (string)dp.GetValue(PasswordProperty);
        }

        public static void SetPassword(DependencyObject dp, string value)
        {
            dp.SetValue(PasswordProperty, value);
        }

        private static bool GetIsUpdating(DependencyObject dp)
        {
            return (bool)dp.GetValue(IsUpdatingProperty);
        }

        private static void SetIsUpdating(DependencyObject dp, bool value)
        {
            dp.SetValue(IsUpdatingProperty, value);
        }

        private static void OnPasswordPropertyChanged(DependencyObject sender, 
            DependencyPropertyChangedEventArgs e)
        {
            PasswordBox? passwordBox = sender as PasswordBox;
            string? password = (string?)e.NewValue;

            if (passwordBox != null && !GetIsUpdating(passwordBox) && password != null)
            {
                passwordBox.Password = password;
            }
        }

        private static void Attach(DependencyObject sender, 
            DependencyPropertyChangedEventArgs e)
        {
            PasswordBox? passwordBox = sender as PasswordBox;

            if (passwordBox != null)
            {
                bool oldValue = (bool)e.OldValue;
                bool newValue = (bool)e.NewValue;

                if (oldValue != newValue)
                {
                    if (newValue)
                    {
                        passwordBox.PasswordChanged += PasswordChanged;
                    }
                    else
                    {
                        passwordBox.PasswordChanged -= PasswordChanged;
                    }
                }
            }
        }

        private static void PasswordChanged(object sender, RoutedEventArgs e)
        {
            PasswordBox? passwordBox = sender as PasswordBox;

            if (passwordBox != null)
            {
                SetIsUpdating(passwordBox, true);
                SetPassword(passwordBox, passwordBox.Password);
                SetIsUpdating(passwordBox, false);
            }
        }
    }
}