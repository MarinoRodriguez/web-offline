using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;

namespace WebOffline.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly IAuditRepository _auditRepository;
    private readonly string _auditSecret;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public AuditService(IAuditRepository auditRepository, IConfiguration configuration)
    {
        _auditRepository = auditRepository;
        _auditSecret = configuration["Audit:SecretKey"] ?? "AuditChangelogTamperProofSecretKey2026!#@";
    }

    public async Task<AuditChangeLog> RecordChangeAsync<T>(
        string userId,
        string userEmail,
        string action,
        string entityType,
        string entityId,
        string description,
        T? oldState,
        T? newState)
    {
        var oldJson = oldState != null ? JsonSerializer.Serialize(oldState, _jsonOptions) : null;
        var newJson = newState != null ? JsonSerializer.Serialize(newState, _jsonOptions) : null;
        var diffJson = ComputeDiffJson(oldState, newState);

        var prevHash = await _auditRepository.GetLatestChangeLogHashAsync();
        var id = Guid.NewGuid().ToString();
        var timestamp = DateTime.UtcNow;
        var timestampIso = timestamp.ToString("O");

        var tamperHash = CalculateTamperHash(
            id,
            timestampIso,
            userId,
            userEmail,
            action,
            entityType,
            entityId,
            diffJson,
            prevHash);

        var changelog = new AuditChangeLog
        {
            Id = id,
            Timestamp = timestamp,
            UserId = userId,
            UserEmail = userEmail,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Description = description,
            OldValuesJson = oldJson,
            NewValuesJson = newJson,
            DiffJson = diffJson,
            PrevHash = prevHash,
            TamperHash = tamperHash
        };

        await _auditRepository.LogChangeAsync(changelog);
        return changelog;
    }

    public string CalculateTamperHash(
        string id,
        string timestampIso,
        string userId,
        string userEmail,
        string action,
        string entityType,
        string entityId,
        string? diffJson,
        string? prevHash)
    {
        var rawData = $"{prevHash ?? "GENESIS"}|{id}|{timestampIso}|{userId}|{userEmail}|{action}|{entityType}|{entityId}|{diffJson ?? "{}"}|{_auditSecret}";
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(rawData);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public bool VerifyChangeLogIntegrity(AuditChangeLog log)
    {
        if (log == null || string.IsNullOrWhiteSpace(log.TamperHash))
            return false;

        var formatsToTry = new[]
        {
            log.Timestamp.Kind == DateTimeKind.Utc ? log.Timestamp.ToString("O") : log.Timestamp.ToUniversalTime().ToString("O"),
            DateTime.SpecifyKind(log.Timestamp, DateTimeKind.Utc).ToString("O"),
            log.Timestamp.ToString("O")
        }.Distinct();

        foreach (var ts in formatsToTry)
        {
            var computed = CalculateTamperHash(
                log.Id,
                ts,
                log.UserId,
                log.UserEmail,
                log.Action,
                log.EntityType,
                log.EntityId,
                log.DiffJson,
                log.PrevHash);

            if (string.Equals(computed, log.TamperHash, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string ComputeDiffJson<T>(T? oldState, T? newState)
    {
        if (oldState == null && newState == null)
            return "{}";

        if (oldState == null)
        {
            return JsonSerializer.Serialize(new
            {
                type = "CREATED",
                state = newState
            }, _jsonOptions);
        }

        if (newState == null)
        {
            return JsonSerializer.Serialize(new
            {
                type = "DELETED",
                state = oldState
            }, _jsonOptions);
        }

        var oldDict = ToPropertyDictionary(oldState);
        var newDict = ToPropertyDictionary(newState);

        var changes = new Dictionary<string, object?>();

        foreach (var kvp in newDict)
        {
            if (!oldDict.TryGetValue(kvp.Key, out var oldVal))
            {
                changes[kvp.Key] = new { from = (object?)null, to = kvp.Value };
            }
            else if (!Equals(oldVal, kvp.Value))
            {
                changes[kvp.Key] = new { from = oldVal, to = kvp.Value };
            }
        }

        return JsonSerializer.Serialize(new
        {
            type = "MODIFIED",
            changedFields = changes
        }, _jsonOptions);
    }

    private static Dictionary<string, object?> ToPropertyDictionary(object obj)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var props = obj.GetType().GetProperties();
        foreach (var prop in props)
        {
            // Ignore sensitive fields from diffing if necessary (e.g. PasswordHash)
            if (prop.Name.Equals("PasswordHash", StringComparison.OrdinalIgnoreCase))
                continue;

            dict[prop.Name] = prop.GetValue(obj);
        }
        return dict;
    }
}
