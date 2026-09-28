using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;
using WebOffline.Infrastructure.Data;

namespace WebOffline.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly ISqliteDbConnectionFactory _connectionFactory;

    public UserRepository(ISqliteDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<User?> GetByIdAsync(string id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<User>(
            @"SELECT id, email, password_hash as PasswordHash, full_name as FullName, 
                     system_role as SystemRole, created_by as CreatedBy, created_at as CreatedAt, 
                     updated_by as UpdatedBy, updated_at as UpdatedAt, is_active as IsActive 
              FROM users WHERE id = @Id;",
            new { Id = id });
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<User>(
            @"SELECT id, email, password_hash as PasswordHash, full_name as FullName, 
                     system_role as SystemRole, created_by as CreatedBy, created_at as CreatedAt, 
                     updated_by as UpdatedBy, updated_at as UpdatedAt, is_active as IsActive 
              FROM users WHERE LOWER(email) = LOWER(@Email);",
            new { Email = email });
    }

    public async Task<IEnumerable<User>> GetAllAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<User>(
            @"SELECT id, email, password_hash as PasswordHash, full_name as FullName, 
                     system_role as SystemRole, created_by as CreatedBy, created_at as CreatedAt, 
                     updated_by as UpdatedBy, updated_at as UpdatedAt, is_active as IsActive 
              FROM users ORDER BY created_at DESC;");
    }

    public async Task<int> CreateAsync(User user)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(
            @"INSERT INTO users (id, email, password_hash, full_name, system_role, created_by, created_at, updated_by, updated_at, is_active)
              VALUES (@Id, @Email, @PasswordHash, @FullName, @SystemRole, @CreatedBy, @CreatedAt, @UpdatedBy, @UpdatedAt, @IsActive);",
            new
            {
                user.Id,
                user.Email,
                user.PasswordHash,
                user.FullName,
                user.SystemRole,
                user.CreatedBy,
                CreatedAt = user.CreatedAt.ToString("O"),
                user.UpdatedBy,
                UpdatedAt = user.UpdatedAt.ToString("O"),
                IsActive = user.IsActive ? 1 : 0
            });
    }

    public async Task<int> UpdateAsync(User user)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(
            @"UPDATE users 
              SET email = @Email, password_hash = @PasswordHash, full_name = @FullName, 
                  system_role = @SystemRole, updated_by = @UpdatedBy, updated_at = @UpdatedAt, is_active = @IsActive
              WHERE id = @Id;",
            new
            {
                user.Id,
                user.Email,
                user.PasswordHash,
                user.FullName,
                user.SystemRole,
                user.UpdatedBy,
                UpdatedAt = DateTime.UtcNow.ToString("O"),
                IsActive = user.IsActive ? 1 : 0
            });
    }

    public async Task<bool> HasAnyAdminAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM users WHERE system_role = 'admin' AND is_active = 1;");
        return count > 0;
    }

    public async Task<int> CountAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM users;");
    }
}
