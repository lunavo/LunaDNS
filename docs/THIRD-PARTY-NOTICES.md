# Thành phần bên thứ ba

## Logo LunaDNS

Hình logo (trăng lưỡi liềm, vệt mạch, khiên) lấy từ mã SVG do PO tạo bằng Gemini (14/11/2026), chép vào `build/icongen`; công cụ này sinh `.ico`, `Assets/Logo.axaml`, `docs/assets/lunadns-mark.svg` và các bản giản lược cho cỡ nhỏ. Logo phát hành theo cùng giấy phép với mã nguồn.

## Fluent UI System Icons

Path data của 12 icon trong `src/LunaDns.App/Assets/Icons.axaml` chép từ <https://github.com/microsoft/fluentui-system-icons> (bản 24 regular).

```text
MIT License

Copyright (c) 2020 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Public Suffix List

Public Suffix List, <https://publicsuffix.org>, Mozilla Public License 2.0. Nhúng nguyên văn tại `src/LunaDns.Core/Domains/public_suffix_list.dat`.
