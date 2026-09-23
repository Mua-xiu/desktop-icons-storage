// 消除 UseWindowsForms 带来的隐式命名空间歧义（统一指向 WPF 类型）
global using Application = System.Windows.Application;
global using UserControl = System.Windows.Controls.UserControl;
global using Point = System.Windows.Point;
global using Color = System.Windows.Media.Color;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using DragEventArgs = System.Windows.DragEventArgs;
global using Clipboard = System.Windows.Clipboard;
global using DataObject = System.Windows.DataObject;
global using DataFormats = System.Windows.DataFormats;
global using DragDrop = System.Windows.DragDrop;
global using DragDropEffects = System.Windows.DragDropEffects;
global using Brushes = System.Windows.Media.Brushes;
global using Brush = System.Windows.Media.Brush;
global using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
global using MessageBox = System.Windows.MessageBox;
