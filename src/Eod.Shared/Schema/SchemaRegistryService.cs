using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Eod.Shared.Configuration;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eod.Shared.Schema;

/// <summary>
/// Service for interacting with Confluent Schema Registry.
/// Supports Protobuf schema registration, validation, and caching.
/// </summary>
public sealed class SchemaRegistryService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly SchemaRegistrySettings _settings;
    private readonly ILogger<SchemaRegistryService> _logger;
    
    // Cache: subject -> (schemaId, schemaString)
    private readonly ConcurrentDictionary<string, SchemaInfo> _schemaCache = new();
    // Cache: schemaId -> schema descriptor
    private readonly ConcurrentDictionary<int, FileDescriptor> _idToDescriptor = new();
    
    private long _schemasRegistered;
    private long _validationsPassed;
    private long _validationsFailed;
    private long _cacheHits;
    private long _cacheMisses;

    public SchemaRegistryService(
        IOptions<SchemaRegistrySettings> settings,
        ILogger<SchemaRegistryService> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_settings.Url),
            Timeout = TimeSpan.FromMilliseconds(_settings.RequestTimeoutMs)
        };

        // Add basic auth if configured
        if (!string.IsNullOrEmpty(_settings.Username) && !string.IsNullOrEmpty(_settings.Password))
        {
            var auth = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.Username}:{_settings.Password}"));
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
        }

        _httpClient.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.schemaregistry.v1+json"));

        _logger.LogInformation(
            "Schema Registry service initialized. URL: {Url}, Enabled: {Enabled}, AutoRegister: {AutoRegister}",
            _settings.Url, _settings.Enabled, _settings.AutoRegisterSchemas);
    }

    /// <summary>
    /// Registers a Protobuf schema with the Schema Registry.
    /// </summary>
    /// <param name="topic">The Kafka topic name</param>
    /// <param name="descriptor">The Protobuf FileDescriptor</param>
    /// <param name="isKey">True for key schema, false for value schema</param>
    /// <returns>The schema ID</returns>
    public async Task<int> RegisterSchemaAsync(
        string topic, 
        FileDescriptor descriptor,
        bool isKey = false,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled || !_settings.AutoRegisterSchemas)
        {
            _logger.LogDebug("Schema registration skipped (disabled)");
            return -1;
        }

        var subject = GetSubjectName(topic, descriptor.Name, isKey);
        
        // Check cache first
        if (_schemaCache.TryGetValue(subject, out var cached))
        {
            Interlocked.Increment(ref _cacheHits);
            return cached.Id;
        }
        Interlocked.Increment(ref _cacheMisses);

        try
        {
            // Build the .proto file content
            var protoContent = BuildProtoFileContent(descriptor);
            
            var request = new SchemaRegistrationRequest
            {
                SchemaType = "PROTOBUF",
                Schema = protoContent
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"/subjects/{subject}/versions",
                request,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SchemaRegistrationResponse>(cancellationToken);
                var schemaId = result?.Id ?? -1;
                
                _schemaCache[subject] = new SchemaInfo(schemaId, protoContent, descriptor);
                Interlocked.Increment(ref _schemasRegistered);
                
                _logger.LogInformation(
                    "Schema registered: Subject={Subject}, Id={SchemaId}",
                    subject, schemaId);
                
                return schemaId;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Schema registration failed: Subject={Subject}, Status={Status}, Error={Error}",
                    subject, response.StatusCode, error);
                return -1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register schema for subject {Subject}", subject);
            return -1;
        }
    }

    /// <summary>
    /// Gets a schema by ID from the registry.
    /// </summary>
    public async Task<string?> GetSchemaByIdAsync(int schemaId, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled) return null;

        try
        {
            var response = await _httpClient.GetAsync($"/schemas/ids/{schemaId}", cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SchemaByIdResponse>(cancellationToken);
                return result?.Schema;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get schema by ID {SchemaId}", schemaId);
            return null;
        }
    }

    /// <summary>
    /// Gets the latest schema version for a subject.
    /// </summary>
    public async Task<SchemaVersionInfo?> GetLatestVersionAsync(
        string subject, 
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled) return null;

        try
        {
            var response = await _httpClient.GetAsync($"/subjects/{subject}/versions/latest", cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<SchemaVersionInfo>(cancellationToken);
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latest version for subject {Subject}", subject);
            return null;
        }
    }

    /// <summary>
    /// Checks if a schema is compatible with the latest version.
    /// </summary>
    public async Task<CompatibilityResult> CheckCompatibilityAsync(
        string topic,
        FileDescriptor descriptor,
        bool isKey = false,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            return new CompatibilityResult { IsCompatible = true, Message = "Schema validation disabled" };
        }

        var subject = GetSubjectName(topic, descriptor.Name, isKey);
        var protoContent = BuildProtoFileContent(descriptor);

        try
        {
            var request = new SchemaRegistrationRequest
            {
                SchemaType = "PROTOBUF",
                Schema = protoContent
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"/compatibility/subjects/{subject}/versions/latest",
                request,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<CompatibilityCheckResponse>(cancellationToken);
                var isCompatible = result?.IsCompatible ?? false;
                
                if (isCompatible)
                {
                    Interlocked.Increment(ref _validationsPassed);
                }
                else
                {
                    Interlocked.Increment(ref _validationsFailed);
                }
                
                return new CompatibilityResult
                {
                    IsCompatible = isCompatible,
                    Message = isCompatible ? "Schema is compatible" : "Schema is incompatible"
                };
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // No previous version exists - this is the first schema
                return new CompatibilityResult { IsCompatible = true, Message = "No previous version (first registration)" };
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                return new CompatibilityResult { IsCompatible = false, Message = $"Compatibility check failed: {error}" };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check compatibility for subject {Subject}", subject);
            return new CompatibilityResult { IsCompatible = false, Message = ex.Message };
        }
    }

    /// <summary>
    /// Lists all registered subjects.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListSubjectsAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled) return Array.Empty<string>();

        try
        {
            var response = await _httpClient.GetAsync("/subjects", cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var subjects = await response.Content.ReadFromJsonAsync<List<string>>(cancellationToken);
                return subjects ?? new List<string>();
            }
            
            return Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list subjects");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Gets all versions for a subject.
    /// </summary>
    public async Task<IReadOnlyList<int>> GetVersionsAsync(string subject, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled) return Array.Empty<int>();

        try
        {
            var response = await _httpClient.GetAsync($"/subjects/{subject}/versions", cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var versions = await response.Content.ReadFromJsonAsync<List<int>>(cancellationToken);
                return versions ?? new List<int>();
            }
            
            return Array.Empty<int>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get versions for subject {Subject}", subject);
            return Array.Empty<int>();
        }
    }

    /// <summary>
    /// Checks if the Schema Registry is available.
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled) return true;

        try
        {
            var response = await _httpClient.GetAsync("/subjects", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets schema registry metrics.
    /// </summary>
    public SchemaRegistryMetrics GetMetrics() => new()
    {
        SchemasRegistered = Interlocked.Read(ref _schemasRegistered),
        ValidationsPassed = Interlocked.Read(ref _validationsPassed),
        ValidationsFailed = Interlocked.Read(ref _validationsFailed),
        CacheHits = Interlocked.Read(ref _cacheHits),
        CacheMisses = Interlocked.Read(ref _cacheMisses),
        CachedSchemas = _schemaCache.Count
    };

    private string GetSubjectName(string topic, string recordName, bool isKey)
    {
        var suffix = isKey ? "-key" : "-value";
        
        return _settings.SubjectNamingStrategy switch
        {
            SubjectNamingStrategy.TopicName => $"{topic}{suffix}",
            SubjectNamingStrategy.RecordName => recordName,
            SubjectNamingStrategy.TopicRecordName => $"{topic}-{recordName}",
            _ => $"{topic}{suffix}"
        };
    }

    private static string BuildProtoFileContent(FileDescriptor descriptor)
    {
        // For Protobuf, we need to provide the .proto file content
        // This is a simplified version - in production you'd read the actual .proto file
        var sb = new StringBuilder();
        sb.AppendLine($"syntax = \"proto3\";");
        sb.AppendLine($"package {descriptor.Package};");
        sb.AppendLine();
        
        // Add C# namespace option if present
        if (!string.IsNullOrEmpty(descriptor.GetOptions()?.CsharpNamespace))
        {
            sb.AppendLine($"option csharp_namespace = \"{descriptor.GetOptions().CsharpNamespace}\";");
            sb.AppendLine();
        }

        // Add message definitions
        foreach (var messageType in descriptor.MessageTypes)
        {
            BuildMessageDefinition(sb, messageType, 0);
        }
        
        // Add enum definitions
        foreach (var enumType in descriptor.EnumTypes)
        {
            BuildEnumDefinition(sb, enumType, 0);
        }

        return sb.ToString();
    }

    private static void BuildMessageDefinition(StringBuilder sb, MessageDescriptor message, int indent)
    {
        var prefix = new string(' ', indent * 2);
        sb.AppendLine($"{prefix}message {message.Name} {{");
        
        foreach (var field in message.Fields.InDeclarationOrder())
        {
            var fieldType = GetFieldTypeName(field);
            var repeated = field.IsRepeated && !field.IsMap ? "repeated " : "";
            sb.AppendLine($"{prefix}  {repeated}{fieldType} {field.Name} = {field.FieldNumber};");
        }
        
        // Nested types
        foreach (var nestedMessage in message.NestedTypes)
        {
            BuildMessageDefinition(sb, nestedMessage, indent + 1);
        }
        
        foreach (var nestedEnum in message.EnumTypes)
        {
            BuildEnumDefinition(sb, nestedEnum, indent + 1);
        }
        
        sb.AppendLine($"{prefix}}}");
        sb.AppendLine();
    }

    private static void BuildEnumDefinition(StringBuilder sb, EnumDescriptor enumType, int indent)
    {
        var prefix = new string(' ', indent * 2);
        sb.AppendLine($"{prefix}enum {enumType.Name} {{");
        
        foreach (var value in enumType.Values)
        {
            sb.AppendLine($"{prefix}  {value.Name} = {value.Number};");
        }
        
        sb.AppendLine($"{prefix}}}");
        sb.AppendLine();
    }

    private static string GetFieldTypeName(FieldDescriptor field)
    {
        return field.FieldType switch
        {
            FieldType.Double => "double",
            FieldType.Float => "float",
            FieldType.Int64 => "int64",
            FieldType.UInt64 => "uint64",
            FieldType.Int32 => "int32",
            FieldType.Fixed64 => "fixed64",
            FieldType.Fixed32 => "fixed32",
            FieldType.Bool => "bool",
            FieldType.String => "string",
            FieldType.Bytes => "bytes",
            FieldType.UInt32 => "uint32",
            FieldType.SFixed32 => "sfixed32",
            FieldType.SFixed64 => "sfixed64",
            FieldType.SInt32 => "sint32",
            FieldType.SInt64 => "sint64",
            FieldType.Enum => field.EnumType.Name,
            FieldType.Message => field.MessageType.Name,
            _ => "bytes"
        };
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    // JSON DTOs for Schema Registry API
    private sealed class SchemaRegistrationRequest
    {
        [JsonPropertyName("schemaType")]
        public string SchemaType { get; init; } = "PROTOBUF";
        
        [JsonPropertyName("schema")]
        public required string Schema { get; init; }
    }

    private sealed class SchemaRegistrationResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }
    }

    private sealed class SchemaByIdResponse
    {
        [JsonPropertyName("schema")]
        public string? Schema { get; init; }
        
        [JsonPropertyName("schemaType")]
        public string? SchemaType { get; init; }
    }

    private sealed class CompatibilityCheckResponse
    {
        [JsonPropertyName("is_compatible")]
        public bool IsCompatible { get; init; }
    }
}

/// <summary>
/// Cached schema information.
/// </summary>
public sealed record SchemaInfo(int Id, string Schema, FileDescriptor Descriptor);

/// <summary>
/// Schema version information from the registry.
/// </summary>
public sealed class SchemaVersionInfo
{
    [JsonPropertyName("subject")]
    public string? Subject { get; init; }
    
    [JsonPropertyName("version")]
    public int Version { get; init; }
    
    [JsonPropertyName("id")]
    public int Id { get; init; }
    
    [JsonPropertyName("schemaType")]
    public string? SchemaType { get; init; }
    
    [JsonPropertyName("schema")]
    public string? Schema { get; init; }
}

/// <summary>
/// Result of a compatibility check.
/// </summary>
public sealed class CompatibilityResult
{
    public bool IsCompatible { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Schema registry operational metrics.
/// </summary>
public sealed class SchemaRegistryMetrics
{
    public long SchemasRegistered { get; init; }
    public long ValidationsPassed { get; init; }
    public long ValidationsFailed { get; init; }
    public long CacheHits { get; init; }
    public long CacheMisses { get; init; }
    public int CachedSchemas { get; init; }
}
