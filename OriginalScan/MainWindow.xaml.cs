﻿using FontAwesome5;
using Microsoft.AspNetCore.Http;
using Notification.Wpf;
using Notification.Wpf.Constants;
using Notification.Wpf.Controls;
using NTwain;
using NTwain.Data;
using OriginalScan.Views;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using ScanApp.Common.Common;
using ScanApp.Common.Settings;
using ScanApp.Data.Entities;
using ScanApp.Intergration.ApiClients;
using ScanApp.Intergration.Constracts;
using ScanApp.Model.Models;
using System;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Printing;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MessageBox = System.Windows.Forms.MessageBox;

namespace OriginalScan
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        public DataSource? dataSource;
        public TwainSession? _twainSession;
        public List<Bitmap> scannedImages;
        public ObservableCollection<BitmapImage> listImages = new ObservableCollection<BitmapImage>();
        private DateTime _scanTime;
        private readonly ITransferApiClient _transferApiClient;
        private readonly ScanContext _context;
        private readonly NotificationManager _notificationManager;

        public MainWindow
        (
            ScanContext context
        )
        {
            InitializeComponent();
            _context = context;
            scannedImages = new List<Bitmap>();
            DataContext = this;
            CreateSession();
            _transferApiClient = new TransferApiClient();
            NotificationConstants.MessagePosition = NotificationPosition.TopRight;
            _notificationManager = new NotificationManager();
            LoadDirectoryTree();
        }

        private void NotificationShow(string type, string message)
        {
            switch (type)
            {
                case "error":
                    {
                        var errorNoti = new NotificationContent
                        {
                            Title = "Lỗi!",
                            Message = $"Có lỗi: {message}",
                            Type = NotificationType.Error,
                            Icon = new SvgAwesome()
                            {
                                Icon = EFontAwesomeIcon.Solid_Times,
                                Height = 25,
                                Foreground = new SolidColorBrush(Colors.Black)
                            },
                            Background = new SolidColorBrush(Colors.Red),
                            Foreground = new SolidColorBrush(Colors.White),
                        };
                        _notificationManager.Show(errorNoti);
                        break;
                    }
                case "success":
                    {
                        var successNoti = new NotificationContent
                        {
                            Title = "Thành công!",
                            Message = $"{message}",
                            Type = NotificationType.Success,
                            Icon = new SvgAwesome()
                            {
                                Icon = EFontAwesomeIcon.Solid_Check,
                                Height = 25,
                                Foreground = new SolidColorBrush(Colors.Black)
                            },
                            Background = new SolidColorBrush(Colors.Green),
                            Foreground = new SolidColorBrush(Colors.White),
                        };
                        _notificationManager.Show(successNoti);
                        break;
                    }
                case "warning":
                    {
                        var warningNoti = new NotificationContent
                        {
                            Title = "Thông báo!",
                            Message = $"{message}",
                            Type = NotificationType.Warning,
                            Icon = new SvgAwesome()
                            {
                                Icon = EFontAwesomeIcon.Solid_ExclamationTriangle,
                                Height = 25,
                                Foreground = new SolidColorBrush(Colors.Black)
                            },
                            Background = new SolidColorBrush(Colors.Yellow),
                            Foreground = new SolidColorBrush(Colors.Black),
                        };
                        _notificationManager.Show(warningNoti);
                        break;
                    }
            }
        }

        private void CreateSession()
        {
            try
            {
                var appId = TWIdentity.CreateFromAssembly(DataGroups.Image, Assembly.GetExecutingAssembly());
                var session = new TwainSession(appId);
                _twainSession = session;
                _twainSession.Open();
            }
            catch (Exception ex)
            {
                NotificationShow("error", ex.Message);
                return;
            }
        }

        public void ScanButton_Click(object sender, EventArgs e)
        {
            try
            {
                if(_twainSession == null)
                {
                    NotificationShow("warning", "Vui lòng kiểm tra lại thiết bị trước khi thực hiện Scan!");
                    return;
                }

                if (dataSource == null)
                {
                    NotificationShow("warning", "Vui lòng kiểm tra lại thiết bị trước khi thực hiện Scan!");
                    return;
                }

                _scanTime = DateTime.Now;

                _twainSession.DataTransferred -= DataTransferred;
                _twainSession.DataTransferred += DataTransferred;

                if(!_twainSession.IsSourceOpen)
                    dataSource.Open();

                if (!_twainSession.IsSourceOpen)
                {
                    NotificationShow("warning", "Vui lòng kiểm tra lại thiết bị trước khi thực hiện Scan!");
                    return;
                }

                dataSource.Capabilities.CapDuplexEnabled.SetValue(BoolType.True);
                dataSource.Enable(SourceEnableMode.NoUI, false, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                NotificationShow("error", ex.Message);
                return;
            }
        }

        private void DataTransferred(object s, DataTransferredEventArgs e)
        {
            try
            {
                DateTime now = _scanTime;
                string userFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string systemPath = System.IO.Path.Combine(FolderSetting.AppFolder, FolderSetting.Images);
                string path = System.IO.Path.Combine(userFolderPath, systemPath, now.ToString("yyyyMMddHHmmss"));
                Directory.CreateDirectory(path);

                if (e.NativeData != IntPtr.Zero)
                {
                    var stream = e.GetNativeImageStream();
                    if (stream != null)
                    {
                        scannedImages.Clear();
                        scannedImages.Add(new Bitmap(stream));
                    }

                    foreach (var img in scannedImages)
                    {
                        BitmapImage bitmapImage = new BitmapImage();

                        using (MemoryStream memory = new MemoryStream())
                        {
                            img.Save(memory, ImageFormat.Png);
                            memory.Position = 0;
                            bitmapImage.BeginInit();
                            bitmapImage.StreamSource = memory;
                            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                            bitmapImage.EndInit();
                            bitmapImage.Freeze();
                        }

                        Guid guid = Guid.NewGuid();
                        string imagesName = now.ToString("yyyyMMddHHmmss") + guid.ToString("N") + ".png";
                        string imagePath = System.IO.Path.Combine(path, imagesName);
                        img.Save(imagePath);

                        App.Current.Dispatcher.Invoke((Action)delegate
                        {
                            listImages.Add(bitmapImage);
                        });
                    }

                    App.Current.Dispatcher.Invoke((Action)delegate
                    {
                        ListMainImages.ItemsSource = listImages;
                        ListExtraImages.ItemsSource = listImages;
                    });
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        public void ConvertToPdfButton_Click(object sender, EventArgs e)
        {
            try
            {
                string userFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string systemPath = System.IO.Path.Combine(FolderSetting.AppFolder, FolderSetting.Images);
                string pdfPath = System.IO.Path.Combine(FolderSetting.AppFolder, FolderSetting.PDFs);
                string defaultPath = System.IO.Path.Combine(userFolderPath, systemPath);

                if (!Directory.Exists(defaultPath))
                    defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                using (var dialog = new FolderBrowserDialog())
                {
                    dialog.SelectedPath = defaultPath;

                    DialogResult result = dialog.ShowDialog();
                    if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                    {
                        string selectedFolder = dialog.SelectedPath;
                        string[] images = Directory.GetFiles(selectedFolder);
                        string folderName = System.IO.Path.GetFileName(selectedFolder);

                        if (images.Count() == 0)
                        {
                            NotificationShow("error", $"Không có hình ảnh trong thư mục!");
                            return;
                        }

                        string pdfFileName = folderName + ".pdf";
                        string folderPath = System.IO.Path.Combine(userFolderPath, pdfPath);
                        Directory.CreateDirectory(folderPath);
                        string pdfFilePath = System.IO.Path.Combine(userFolderPath, pdfPath, pdfFileName);

                        if (File.Exists(pdfFilePath))
                        {
                            MessageBoxResult pdfConfirm = System.Windows.MessageBox.Show("Đã tồn tại một tệp PDF có cùng tên. Bạn có muốn thay thế nó?", "Thông báo!", MessageBoxButton.YesNo, MessageBoxImage.Question);

                            if (pdfConfirm == MessageBoxResult.Yes)
                                File.Delete(pdfFileName);
                            else
                                return;

                            _notificationManager.ShowButtonWindow("Đã tồn tại một tệp PDF có cùng tên. Bạn có muốn thay thế nó?", "Xác nhận",
                            async () =>
                            {
                                try
                                {
                                    File.Delete(pdfFileName);
                                }
                                catch (Exception ex)
                                {
                                    NotificationShow("error", $"{ex.Message}");
                                    return;

                                }
                            }, "OK", () => { }, "Cancel");
                        }

                        PdfDocument pdfDocument = new PdfDocument();

                        foreach (string imagePath in images)
                        {
                            PdfPage page = pdfDocument.AddPage();

                            using (var image = XImage.FromFile(imagePath))
                            {
                                XGraphics gfx = XGraphics.FromPdfPage(page);
                                gfx.DrawImage(image, 0, 0, page.Width, page.Height);
                            }
                        }

                        pdfDocument.Save(pdfFilePath);
                        NotificationShow("success", $"Lưu thành công tại đường dẫn: {pdfFilePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                NotificationShow("error", ex.Message);
                return;
            }
        }

        public async void TransferToPortal_Click(object sender, EventArgs e)
        {
            try
            {
                string userFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string systemPath = System.IO.Path.Combine(FolderSetting.AppFolder, FolderSetting.Images);
                string pdfPath = System.IO.Path.Combine(FolderSetting.AppFolder, FolderSetting.PDFs);
                string defaultPath = System.IO.Path.Combine(userFolderPath, pdfPath);

                if (!Directory.Exists(defaultPath))
                    defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.InitialDirectory = defaultPath;
                openFileDialog.Filter = "PDF files (*.pdf)|*.pdf";

                DialogResult result = openFileDialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(openFileDialog.FileName))
                {
                    string selectedFolder = openFileDialog.FileName;
                    bool transferResult =  await _transferApiClient.TransferToPortal(selectedFolder);

                    if(transferResult)
                        NotificationShow("success", $"Upload công văn thành công!");
                    else
                    {
                        NotificationShow("error", "Upload công văn thất bại!");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                NotificationShow("error", ex.Message);
                return;
            }
        }

        private void ChooseDevice_Click(object sender, RoutedEventArgs e)
        {
            DeviceWindow deviceWindow = new DeviceWindow();
            deviceWindow.twainSession = _twainSession;
            deviceWindow.mainWindow = this;
            deviceWindow.GetListDevice();
            deviceWindow.ShowDialog();
        }

        private void OpenBatch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BatchWindow batchWindow = new BatchWindow(_context);
                batchWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                NotificationShow("error", ex.Message);
                return;
            }
        }

        public void LoadDirectoryTree()
        {
            trvBatchExplorer.Items.Clear();
            try
            {
                string userFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                userFolderPath = userFolderPath.Replace("/", "\\");
                string path = System.IO.Path.Combine(userFolderPath, FolderSetting.AppFolder, FolderSetting.TempData);

                foreach (string directory in Directory.GetDirectories(path))
                {

                    var directoryInfo = new DirectoryInfo(directory);
                    var directoryItem = new TreeViewItem()
                    {
                        Header = new StackPanel()
                        {
                            Orientation = System.Windows.Controls.Orientation.Horizontal,
                            Children =
                            {
                                new System.Windows.Controls.Image()
                                {
                                    Source = new BitmapImage(new Uri("/Resource/Images/foldericon.png", UriKind.Relative)),
                                    Width = 16,
                                    Height = 16,
                                    Margin = new Thickness(0, 10, 5, 0)
                                },
                                new TextBlock()
                                {
                                    Text = directoryInfo.Name,
                                    Margin = new Thickness(0, 10, 0, 0)
                                }
                            }
                        }
                    };
                    trvBatchExplorer.Items.Add(directoryItem);
                    LoadDirectory(directoryItem, directory);
                }
            }
            catch (Exception ex)
            {
                NotificationShow("error", ex.Message);
            }
        }

        private void LoadDirectory(TreeViewItem parentItem, string folderPath)
        {
            try
            {
                foreach (string directory in Directory.GetDirectories(folderPath))
                {
                    var directoryInfo = new DirectoryInfo(directory);
                    string directoryName = directoryInfo.Name;

                    if (!IsItemAlreadyExists(parentItem, directoryName))
                    {
                        var directoryItem = new TreeViewItem()
                        {
                            Header = new StackPanel()
                            {
                                Orientation = System.Windows.Controls.Orientation.Horizontal,
                                Children =
                        {
                            new System.Windows.Controls.Image()
                            {
                                Source = new BitmapImage(new Uri("/Resource/Images/documents.png", UriKind.Relative)),
                                Width = 16,
                                Height = 16,
                                Margin = new Thickness(0, 10, 5, 0)
                            },
                            new TextBlock()
                            {
                                Text = directoryName,
                                Margin = new Thickness(0, 10, 0, 0)
                            }
                        }
                            }
                        };
                        parentItem.Items.Add(directoryItem);
                        LoadDirectory(directoryItem, directory);
                    }
                }

                foreach (string file in Directory.GetFiles(folderPath))
                {
                    var fileInfo = new FileInfo(file);
                    string fileName = fileInfo.Name;

                    if (!IsItemAlreadyExists(parentItem, fileName))
                    {
                        var fileItem = new TreeViewItem()
                        {
                            Header = new StackPanel()
                            {
                                Orientation = System.Windows.Controls.Orientation.Horizontal,
                                Children =
                        {
                            new System.Windows.Controls.Image()
                            {
                                Source = new BitmapImage(new Uri("/Resource/Icons/crop.png", UriKind.Relative)),
                                Width = 16,
                                Height = 16,
                                Margin = new Thickness(0, 10, 5, 0)
                            },
                            new TextBlock()
                            {
                                Text = fileName,
                                Margin = new Thickness(0, 10, 0, 0)
                            }
                        }
                            }
                        };
                        parentItem.Items.Add(fileItem);
                    }
                }
            }
            catch (Exception ex)
            {
                NotificationShow("error", ex.Message);
            }
        }

        private bool IsItemAlreadyExists(TreeViewItem parentItem, string itemName)
        {
            foreach (TreeViewItem item in parentItem.Items)
            {
                if (item.Header is StackPanel stackPanel)
                {
                    var textBlock = stackPanel.Children.OfType<TextBlock>().FirstOrDefault();
                    if (textBlock != null && textBlock.Text == itemName)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public void ExpandTreeViewItem(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                    return;

                ExpandTreeViewItem(trvBatchExplorer.Items, folderPath);
            }
            catch (Exception ex)
            {
                NotificationShow("error", ex.Message);
            }
        }

        private bool IsTreeViewItemExpanded(TreeViewItem treeViewItem)
        {
            if (treeViewItem == null)
                return false;

            return treeViewItem.IsExpanded;
        }

        private void ExpandTreeViewItem(ItemCollection items, string folderPath)
        {
            foreach (var item in items)
            {
                if (item is TreeViewItem treeViewItem && treeViewItem.Header is StackPanel stackPanel)
                {
                    if (stackPanel.Children.OfType<TextBlock>().FirstOrDefault()?.Text == Path.GetFileName(folderPath))
                    {
                        if (!IsTreeViewItemExpanded(treeViewItem))
                        {
                            treeViewItem.IsExpanded = true;
                            LoadDirectory(treeViewItem, folderPath);
                        }
                        return;
                    }

                    ExpandTreeViewItem(treeViewItem.Items, folderPath);
                }
            }
        }
    }
}