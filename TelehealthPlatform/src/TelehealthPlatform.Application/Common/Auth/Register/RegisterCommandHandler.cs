using MediatR;
using TelehealthPlatform.Application.Common.Interfaces;
using TelehealthPlatform.Domain.Entities;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Application.Auth.Register;

/// <summary>
/// Creates User + the matching empty Profile in one transaction
/// (API Contract, POST /auth/register). ConsultantProfile.CreateEmpty no
/// longer takes a timeZoneId — TimeZoneId starts null and is required
/// before PUT /consultants/me/availability will work (resolved decision,
/// see API Contract v2's timezone-required precondition).
/// </summary>
public class RegisterCommandHandler(
    IUserRepository userRepository,
    IPatientProfileRepository patientProfileRepository,
    IConsultantProfileRepository consultantProfileRepository,
    IPasswordHasher passwordHasher,
    ITokenHasher tokenHasher,
    ISecureTokenGenerator secureTokenGenerator,
    IEmailService emailService,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock) : IRequestHandler<RegisterCommand, RegisterResult>
{
    public async Task<RegisterResult> Handle(RegisterCommand request, CancellationToken ct)
    {
        var existing = await userRepository.GetByEmailAsync(request.Email, ct);
        if (existing is not null)
            throw new InvalidOperationException("Email already registered.");

        var role = Enum.Parse<UserRole>(request.Role);
        var passwordHash = passwordHasher.Hash(request.Password);

        var rawVerificationToken = secureTokenGenerator.Generate();
        var verificationTokenHash = tokenHasher.Hash(rawVerificationToken);

        var user = User.Register(request.Name, request.Email, passwordHash, role, verificationTokenHash, clock.UtcNow);
        await userRepository.AddAsync(user, ct);

        // Same transaction — Requirements §3.1: "every User has exactly
        // one matching profile from the moment registration succeeds."
        // Both AddAsync calls + SaveChangesAsync below commit together.
        if (role == UserRole.Patient)
        {
            var profile = PatientProfile.CreateEmpty(user.Id);
            await patientProfileRepository.AddAsync(profile, ct);
        }
        else
        {
            var profile = ConsultantProfile.CreateEmpty(user.Id); // no timeZoneId — set later via PUT /consultants/me
            await consultantProfileRepository.AddAsync(profile, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);

        await emailService.SendEmailVerificationAsync(user.Email, rawVerificationToken, ct);

        return new RegisterResult(user.Id, request.Role);
    }
}