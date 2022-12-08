![img|300x200](https://raw.githubusercontent.com/managedcode/MimeTypes/main/logo.png)

# MimeTypes

[![.NET](https://github.com/managedcode/MimeTypes/actions/workflows/dotnet.yml/badge.svg)](https://github.com/managedcode/MimeTypes/actions/workflows/dotnet.yml)
[![Coverage Status](https://coveralls.io/repos/github/managedcode/MimeTypes/badge.svg?branch=main&service=github)](https://coveralls.io/github/managedcode/MimeTypes?branch=main)
[![nuget](https://github.com/managedcode/MimeTypes/actions/workflows/nuget.yml/badge.svg?branch=main)](https://github.com/managedcode/MimeTypes/actions/workflows/nuget.yml)
[![CodeQL](https://github.com/managedcode/MimeTypes/actions/workflows/codeql-analysis.yml/badge.svg?branch=main)](https://github.com/managedcode/MimeTypes/actions/workflows/codeql-analysis.yml)

| Version | Package                                                                                                                             | Description     |
| ------- |-------------------------------------------------------------------------------------------------------------------------------------|-----------------|
|[![NuGet Package](https://img.shields.io/nuget/v/ManagedCode.MimeTypes.svg)](https://www.nuget.org/packages/ManagedCode.MimeTypes) | [ManagedCode.MimeTypes](https://www.nuget.org/packages/ManagedCode.MimeTypes)                                                   | Core            |

---


## Motivation
MIME (Multipurpose Internet Mail Extensions) types are used to specify the type of data that a file contains, such as text, images, or video. These types are often used in web development to indicate the type of content in HTTP responses.

Working with MIME types in C# can be cumbersome, as they are typically represented as strings. This can make it difficult to ensure the correct usage and spelling of MIME types, and can lead to errors and inconsistencies in your code.

Our project, MimeType, provides a convenient way to work with MIME types in C#. It defines a set of properties for each MIME type, allowing you to use properties instead of strings in your code. This makes it easy to ensure the correct usage and spelling of MIME types, and can make your code more readable and maintainable.

## Features
Defines a set of properties for each MIME type, allowing you to use properties instead of strings in your code.
Makes it easy to ensure the correct usage and spelling of MIME types.
Improves the readability and maintainability of your code.

## Example
Here's an example of how you might use the MimeType project to specify the content type of an HTTP response in C#:

``` csharp
using ManagedCode.MimeTypes;
```
``` csharp
// Set the content type of the response to "text/plain".
response.ContentType = MimeType.TextPlain;
```

## Installation
To install the MimeType project, you can use NuGet:

``` csharp
dotnet add package ManagedCode.MimeTypes
```

## Usage
To use the MimeType project, you will need to add a reference to the MimeType namespace in your C# code:

``` csharp
using MimeType;
```
Then, you can use the properties defined by the MimeType class to specify MIME types in your code. For example:

``` csharp
// Set the content type of the response to "application/pdf".
response.ContentType = MimeHelper.PDF;

// Set the content type of the response to ""text/plain"".
response.ContentType = MimeHelper.GetMimeType("file.txt");
```

## Conclusion
In summary, the MimeType project provides a convenient and easy-to-use way to work with MIME types in C#. Its properties make it easy to ensure the correct usage and spelling of MIME types, and can improve the readability and maintainability of your code. We hope you find it useful in your own projects!
