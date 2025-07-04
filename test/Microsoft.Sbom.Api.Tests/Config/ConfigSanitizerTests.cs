// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Sbom.Api.Config;
using Microsoft.Sbom.Api.Exceptions;
using Microsoft.Sbom.Api.Hashing;
using Microsoft.Sbom.Api.Utils;
using Microsoft.Sbom.Common;
using Microsoft.Sbom.Common.Config;
using Microsoft.Sbom.Contracts.Enums;
using Microsoft.Sbom.Extensions.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PowerArgs;
using Constants = Microsoft.Sbom.Api.Utils.Constants;

namespace Microsoft.Sbom.Api.Tests.Config;

[TestClass]
public class ConfigSanitizerTests
{
    private Mock<IFileSystemUtils> mockFileSystemUtils;
    private Mock<IHashAlgorithmProvider> mockHashAlgorithmProvider;
    private Mock<IAssemblyConfig> mockAssemblyConfig;
    private ConfigSanitizer configSanitizer;
    private readonly bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    [TestInitialize]
    public void Initialize()
    {
        mockFileSystemUtils = new Mock<IFileSystemUtils>();
        mockFileSystemUtils
            .Setup(f => f.JoinPaths(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string p1, string p2) => Path.Join(p1, p2));

        mockHashAlgorithmProvider = new Mock<IHashAlgorithmProvider>();
        mockHashAlgorithmProvider
            .Setup(h => h.Get(It.IsAny<string>()))
            .Returns((string a) =>
            {
                if (a == "SHA256")
                {
                    return new AlgorithmName(a, stream => SHA256.Create().ComputeHash(stream));
                }

                throw new UnsupportedHashAlgorithmException("Unsupported");
            });

        mockAssemblyConfig = new Mock<IAssemblyConfig>();

        configSanitizer = new ConfigSanitizer(mockHashAlgorithmProvider.Object, mockFileSystemUtils.Object, mockAssemblyConfig.Object);
    }

    /// <summary>
    /// This method returns a configuration object with all the properties set to standard values,
    /// which won't make the test fail. Change one value that you are testing in order to ensure you
    /// are testing the correct config.
    /// </summary>
    private Configuration GetConfigurationBaseObject()
    {
        return new Configuration
        {
            HashAlgorithm = new ConfigurationSetting<AlgorithmName>
            {
                Source = SettingSource.CommandLine,
                Value = new AlgorithmName("SHA256", null)
            },
            BuildDropPath = new ConfigurationSetting<string>
            {
                Source = SettingSource.Default,
                Value = "dropPath"
            },
            ManifestInfo = new ConfigurationSetting<IList<ManifestInfo>>
            {
                Source = SettingSource.Default,
                Value = new List<ManifestInfo>
                { Constants.TestManifestInfo }
            },
            Verbosity = new ConfigurationSetting<Serilog.Events.LogEventLevel>
            {
                Source = SettingSource.Default,
                Value = Serilog.Events.LogEventLevel.Information
            },
            Conformance = new ConfigurationSetting<ConformanceType>
            {
                Source = SettingSource.Default,
                Value = ConformanceType.None,
            },
        };
    }

    [TestMethod]
    public void SetValueForManifestInfoForValidation_Succeeds()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.Validate;
        configSanitizer.SanitizeConfig(config);

        mockAssemblyConfig.Verify();
    }

    [TestMethod]
    public void NoValueForManifestInfoForValidation_Throws()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.Validate;
        config.ManifestInfo.Value.Clear();

        Assert.ThrowsException<ValidationArgException>(() => configSanitizer.SanitizeConfig(config));
    }

    [TestMethod]
    public void SetValueForConformanceWithValidManifestInfoForValidation_Succeeds()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.Validate;
        config.ManifestInfo.Value = new List<ManifestInfo> { Constants.SPDX30ManifestInfo };
        config.Conformance = new ConfigurationSetting<ConformanceType>
        {
            Source = SettingSource.CommandLine,
            Value = ConformanceType.NTIAMin
        };

        configSanitizer.SanitizeConfig(config);
        Assert.AreEqual(ConformanceType.NTIAMin, config.Conformance.Value);
    }

    [TestMethod]
    public void SetNoneValueForConformanceWithValidManifestInfoForValidation_Succeeds()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.Validate;
        config.ManifestInfo.Value = new List<ManifestInfo> { Constants.SPDX30ManifestInfo };
        config.Conformance = new ConfigurationSetting<ConformanceType>
        {
            Source = SettingSource.CommandLine,
            Value = ConformanceType.None
        };

        configSanitizer.SanitizeConfig(config);
        Assert.AreEqual(ConformanceType.None, config.Conformance.Value);
    }

    [TestMethod]
    public void SetValueForConformanceWithInvalidManifestInfoForValidation_Throws()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.Validate;
        config.Conformance = new ConfigurationSetting<ConformanceType>
        {
            Source = SettingSource.CommandLine,
            Value = ConformanceType.NTIAMin
        };

        var exception = Assert.ThrowsException<ValidationArgException>(() => configSanitizer.SanitizeConfig(config));
        Assert.IsTrue(exception.Message.Contains("Please use a supported combination."));
    }

    [TestMethod]
    public void NoValueForConformanceWithValidManifestInfoForValidation_Succeeds()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.Validate;
        config.ManifestInfo.Value = new List<ManifestInfo> { Constants.SPDX30ManifestInfo };
        config.Conformance.Value = null;

        configSanitizer.SanitizeConfig(config);
        Assert.AreEqual(ConformanceType.None, config.Conformance.Value);
    }

    [TestMethod]
    public void NoValueForConformanceWithInvalidManifestInfoForValidation_Succeeds()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.Validate;
        config.ManifestInfo.Value = new List<ManifestInfo> { Constants.SPDX22ManifestInfo };
        config.Conformance.Value = null;

        configSanitizer.SanitizeConfig(config);
        Assert.AreEqual(ConformanceType.None, config.Conformance.Value);
    }

    [TestMethod]
    public void SetValueForManifestInfoForGeneration_Succeeds()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.Generate;
        configSanitizer.SanitizeConfig(config);

        mockAssemblyConfig.Verify();
    }

    [TestMethod]
    public void NoValueForManifestInfoForGeneration_Succeeds()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.Generate;
        config.ManifestInfo.Value.Clear();
        configSanitizer.SanitizeConfig(config);

        mockAssemblyConfig.Verify();
    }

    [TestMethod]
    public void NoValueForBuildDropPathForRedaction_Succeeds()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.Redact;
        config.BuildDropPath = null;

        configSanitizer.SanitizeConfig(config);
    }

    [TestMethod]
    public void NoValueForBuildDropPathForValidateFormat_Succeeds()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.ValidateFormat;
        config.BuildDropPath = null;
        config.SbomPath = new ConfigurationSetting<string>
        {
            Source = SettingSource.Default,
            Value = "any non empty value"
        };

        configSanitizer.SanitizeConfig(config);
    }

    [TestMethod]
    public void NoValueForSbomPathForValidateFormat_Throws()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.ValidateFormat;
        config.SbomPath = null;

        Assert.ThrowsException<ValidationArgException>(() => configSanitizer.SanitizeConfig(config));
    }

    [TestMethod]
    public void NoValueForManifestInfoForValidation_SetsDefaultValue()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.Validate;
        config.ManifestInfo.Value.Clear();
        mockAssemblyConfig.SetupGet(a => a.DefaultManifestInfoForValidationAction).Returns(Constants.TestManifestInfo);

        var sanitizedConfig = configSanitizer.SanitizeConfig(config);

        Assert.IsNotNull(sanitizedConfig.ManifestInfo.Value);
        Assert.AreEqual(1, sanitizedConfig.ManifestInfo.Value.Count);
        Assert.AreEqual(Constants.TestManifestInfo, sanitizedConfig.ManifestInfo.Value.First());

        mockAssemblyConfig.VerifyGet(a => a.DefaultManifestInfoForValidationAction);
    }

    [TestMethod]
    public void NoValueForManifestInfoForGeneration_SetsDefaultValue()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.Generate;
        config.ManifestInfo.Value.Clear();
        mockAssemblyConfig.SetupGet(a => a.DefaultManifestInfoForGenerationAction).Returns(Constants.TestManifestInfo);

        var sanitizedConfig = configSanitizer.SanitizeConfig(config);

        Assert.IsNotNull(sanitizedConfig.ManifestInfo.Value);
        Assert.AreEqual(1, sanitizedConfig.ManifestInfo.Value.Count);
        Assert.AreEqual(Constants.TestManifestInfo, sanitizedConfig.ManifestInfo.Value.First());

        mockAssemblyConfig.VerifyGet(a => a.DefaultManifestInfoForGenerationAction);
    }

    [TestMethod]
    public void ForGenerateActionIgnoresEmptyAlgorithmName_Succeeds()
    {
        var config = GetConfigurationBaseObject();
        config.HashAlgorithm = null;
        config.ManifestToolAction = ManifestToolActions.Generate;
        var sanitizedConfig = configSanitizer.SanitizeConfig(config);

        Assert.IsNull(sanitizedConfig.HashAlgorithm);
    }

    [TestMethod]
    public void ForValidateGetsRealAlgorithmName_Succeeds_DoesNotThrow()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.Validate;
        var sanitizedConfig = configSanitizer.SanitizeConfig(config);

        Assert.IsNotNull(sanitizedConfig.HashAlgorithm);

        var result = config.HashAlgorithm.Value.ComputeHash(TestUtils.GenerateStreamFromString("Hekki"));
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void ForValidateBadAlgorithmNameGetsRealAlgorithmName_Throws()
    {
        var config = GetConfigurationBaseObject();
        config.HashAlgorithm.Value = new AlgorithmName("a", null);
        config.ManifestToolAction = ManifestToolActions.Validate;
        Assert.ThrowsException<UnsupportedHashAlgorithmException>(() => configSanitizer.SanitizeConfig(config));
    }

    [TestMethod]
    public void NullManifestDirShouldUseDropPath_Succeeds()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.Validate;
        config.ManifestDirPath = null;
        configSanitizer.SanitizeConfig(config);

        Assert.IsNotNull(config.ManifestDirPath);
        Assert.IsNotNull(config.ManifestDirPath.Value);

        var expectedPath = Path.Join("dropPath", "_manifest");
        Assert.AreEqual(Path.GetFullPath(expectedPath), Path.GetFullPath(config.ManifestDirPath.Value));
    }

    [TestMethod]
    public void ManifestDirShouldEndWithManifestDirForGenerate_Succeeds()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestDirPath = new ConfigurationSetting<string>("manifestDirPath");

        config.ManifestToolAction = ManifestToolActions.Generate;
        configSanitizer.SanitizeConfig(config);

        Assert.IsNotNull(config.ManifestDirPath);
        Assert.IsNotNull(config.ManifestDirPath.Value);

        var expectedPath = Path.Join("manifestDirPath", "_manifest");
        Assert.AreEqual(Path.GetFullPath(expectedPath), Path.GetFullPath(config.ManifestDirPath.Value));
    }

    [TestMethod]
    public void ManifestDirShouldNotAddManifestDirForValidate_Succeeds()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestDirPath = new ConfigurationSetting<string>
        {
            Source = SettingSource.Default,
            Value = "manifestDirPath"
        };

        config.ManifestToolAction = ManifestToolActions.Validate;
        configSanitizer.SanitizeConfig(config);

        Assert.IsNotNull(config.ManifestDirPath);
        Assert.IsNotNull(config.ManifestDirPath.Value);
        Assert.AreEqual("manifestDirPath", config.ManifestDirPath.Value);
    }

    [TestMethod]
    public void NullDefaultNamespaceUriBaseShouldReturnExistingValue_Succeeds()
    {
        mockAssemblyConfig.SetupGet(a => a.DefaultSbomNamespaceBaseUri).Returns(string.Empty);
        var config = GetConfigurationBaseObject();
        config.NamespaceUriBase = new ConfigurationSetting<string>
        {
            Source = SettingSource.Default,
            Value = "http://base.uri"
        };

        config.ManifestToolAction = ManifestToolActions.Generate;
        configSanitizer.SanitizeConfig(config);

        Assert.AreEqual("http://base.uri", config.NamespaceUriBase.Value);

        mockAssemblyConfig.VerifyGet(a => a.DefaultSbomNamespaceBaseUri);
    }

    [TestMethod]
    public void UserProviderNamespaceUriBaseShouldReturnProvidedValue_Succeeds()
    {
        mockAssemblyConfig.SetupGet(a => a.DefaultSbomNamespaceBaseUri).Returns("http://internal.base.uri");
        var providedNamespaceValue = "http://base.uri";
        var config = GetConfigurationBaseObject();
        config.NamespaceUriBase = new ConfigurationSetting<string>
        {
            Source = SettingSource.CommandLine,
            Value = providedNamespaceValue
        };

        config.ManifestToolAction = ManifestToolActions.Generate;
        configSanitizer.SanitizeConfig(config);

        Assert.AreEqual(providedNamespaceValue, config.NamespaceUriBase.Value);
        Assert.AreEqual(SettingSource.CommandLine, config.NamespaceUriBase.Source);

        mockAssemblyConfig.VerifyGet(a => a.DefaultSbomNamespaceBaseUri);
    }

    [TestMethod]
    public void ShouldGetPackageSupplierFromAsseblyConfig_Succeeds()
    {
        var organization = "Contoso International";
        mockAssemblyConfig.SetupGet(a => a.DefaultPackageSupplier).Returns(organization);
        var config = GetConfigurationBaseObject();

        config.ManifestToolAction = ManifestToolActions.Validate;
        configSanitizer.SanitizeConfig(config);

        Assert.AreEqual(organization, config.PackageSupplier.Value);

        mockAssemblyConfig.VerifyGet(a => a.DefaultPackageSupplier);
    }

    [TestMethod]
    public void ShouldNotOverridePackageSupplierIfProvided_Succeeds()
    {
        var organization = "Contoso International";
        var actualOrg = "Contoso";
        mockAssemblyConfig.SetupGet(a => a.DefaultPackageSupplier).Returns(organization);
        var config = GetConfigurationBaseObject();
        config.PackageSupplier = new ConfigurationSetting<string>
        {
            Source = SettingSource.CommandLine,
            Value = actualOrg
        };

        config.ManifestToolAction = ManifestToolActions.Validate;
        configSanitizer.SanitizeConfig(config);

        Assert.AreEqual(config.PackageSupplier.Value, actualOrg);
    }

    [TestMethod]
    [DataRow(ManifestToolActions.Validate)]
    [DataRow(ManifestToolActions.Generate)]
    public void ConfigSantizer_Validate_ReplacesBackslashes_Linux(ManifestToolActions action)
    {
        if (!isWindows)
        {
            var config = GetConfigurationBaseObject();
            config.ManifestDirPath = new($"\\{nameof(config.ManifestDirPath)}\\", SettingSource.Default);
            config.BuildDropPath = new($"\\{nameof(config.BuildDropPath)}\\", SettingSource.Default);
            config.OutputPath = new($"\\{nameof(config.OutputPath)}\\", SettingSource.Default);
            config.ConfigFilePath = new($"\\{nameof(config.ConfigFilePath)}\\", SettingSource.Default);
            config.RootPathFilter = new($"\\{nameof(config.RootPathFilter)}\\", SettingSource.Default);
            config.BuildComponentPath = new($"\\{nameof(config.BuildComponentPath)}\\", SettingSource.Default);
            config.CatalogFilePath = new($"\\{nameof(config.CatalogFilePath)}\\", SettingSource.Default);
            config.TelemetryFilePath = new($"\\{nameof(config.TelemetryFilePath)}\\", SettingSource.Default);

            config.ManifestToolAction = action;
            configSanitizer.SanitizeConfig(config);

            Assert.IsTrue(config.ManifestDirPath.Value.StartsWith($"/{nameof(config.ManifestDirPath)}/", StringComparison.Ordinal));
            Assert.IsTrue(config.BuildDropPath.Value.StartsWith($"/{nameof(config.BuildDropPath)}/", StringComparison.Ordinal));
            Assert.IsTrue(config.OutputPath.Value.StartsWith($"/{nameof(config.OutputPath)}/", StringComparison.Ordinal));
            Assert.IsTrue(config.ConfigFilePath.Value.StartsWith($"/{nameof(config.ConfigFilePath)}/", StringComparison.Ordinal));
            Assert.IsTrue(config.RootPathFilter.Value.StartsWith($"/{nameof(config.RootPathFilter)}/", StringComparison.Ordinal));
            Assert.IsTrue(config.BuildComponentPath.Value.StartsWith($"/{nameof(config.BuildComponentPath)}/", StringComparison.Ordinal));
            Assert.IsTrue(config.CatalogFilePath.Value.StartsWith($"/{nameof(config.CatalogFilePath)}/", StringComparison.Ordinal));
            Assert.IsTrue(config.TelemetryFilePath.Value.StartsWith($"/{nameof(config.TelemetryFilePath)}/", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    [DataRow(1, DisplayName = "Minimum value of 1")]
    [DataRow(Common.Constants.MaxLicenseFetchTimeoutInSeconds, DisplayName = "Maximum Value of 86400")]
    public void LicenseInformationTimeoutInSeconds_SanitizeMakesNoChanges(int value)
    {
        var config = GetConfigurationBaseObject();
        config.LicenseInformationTimeoutInSeconds = new(value, SettingSource.CommandLine);

        configSanitizer.SanitizeConfig(config);

        Assert.AreEqual(value, config.LicenseInformationTimeoutInSeconds.Value, "The value of LicenseInformationTimeoutInSeconds should remain the same through the sanitization process");
    }

    [TestMethod]
    [DataRow(int.MinValue, Common.Constants.DefaultLicenseFetchTimeoutInSeconds, DisplayName = "Negative Value is changed to Default")]
    [DataRow(0, Common.Constants.DefaultLicenseFetchTimeoutInSeconds, DisplayName = "Zero is changed to Default")]
    [DataRow(Common.Constants.MaxLicenseFetchTimeoutInSeconds + 1, Common.Constants.MaxLicenseFetchTimeoutInSeconds, DisplayName = "Max Value + 1 is truncated")]
    [DataRow(int.MaxValue, Common.Constants.MaxLicenseFetchTimeoutInSeconds, DisplayName = "int.MaxValue is truncated")]
    public void LicenseInformationTimeoutInSeconds_SanitizeExceedsLimits(int value, int expected)
    {
        var config = GetConfigurationBaseObject();
        config.LicenseInformationTimeoutInSeconds = new(value, SettingSource.CommandLine);

        configSanitizer.SanitizeConfig(config);

        Assert.AreEqual(expected, config.LicenseInformationTimeoutInSeconds.Value, "The value of LicenseInformationTimeoutInSeconds should be sanitized to a valid value");
    }

    [TestMethod]
    public void LicenseInformationTimeoutInSeconds_SanitizeNull()
    {
        var config = GetConfigurationBaseObject();
        config.LicenseInformationTimeoutInSeconds = null;

        configSanitizer.SanitizeConfig(config);

        Assert.AreEqual(
            Common.Constants.DefaultLicenseFetchTimeoutInSeconds,
            config.LicenseInformationTimeoutInSeconds.Value,
            $"The value of LicenseInformationTimeoutInSeconds should be set to {Common.Constants.DefaultLicenseFetchTimeoutInSeconds}s when null");

        Assert.AreEqual(SettingSource.Default, config.LicenseInformationTimeoutInSeconds.Source, "The source of LicenseInformationTimeoutInSeconds should be set to Default when null");
    }

    [TestMethod]
    [DataRow(false, "no artifactInfoMap exists")]
    [DataRow(true, "empty artifactInfoMap exists")]
    public void ArtifactMapInfo_InvalidCases_SanitizeThrowsException(bool specifyEmptyArtifactInfoMap, string description)
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.Consolidate;

        if (specifyEmptyArtifactInfoMap)
        {
            config.ArtifactInfoMap = new ConfigurationSetting<Dictionary<string, ArtifactInfo>>
            {
                Source = SettingSource.Default,
                Value = new Dictionary<string, ArtifactInfo>(),
            };
        }

        var e = Assert.ThrowsException<ValidationArgException>(() => configSanitizer.SanitizeConfig(config), $"Sanitizer should throw when {description}");
        Assert.IsTrue(e.Message.Contains("ArtifactInfoMap"), $"Exception message should mention ArtifactMapInfo when {description}");
    }

    [TestMethod]
    public void ArtifactMapInfo_ExistsWithValidData_Consolidate_SanitizeSucceeds()
    {
        var config = GetConfigurationBaseObject();
        config.ManifestToolAction = ManifestToolActions.Consolidate;
        var artifactInfoMap = new Dictionary<string, ArtifactInfo>
        {
            { "artifact1", new ArtifactInfo
                {
                    ExternalManifestDir = "externalManifestDir",
                    IgnoreMissingFiles = true,
                    SkipSigningCheck = false,
                }
            },
            { "artifact2", new ArtifactInfo() },
        };
        config.ArtifactInfoMap = new ConfigurationSetting<Dictionary<string, ArtifactInfo>>
        {
            Source = SettingSource.Default,
            Value = artifactInfoMap,
        };

        var sanitizedConfig = configSanitizer.SanitizeConfig(config);

        Assert.AreSame(artifactInfoMap, sanitizedConfig.ArtifactInfoMap.Value, "ArtifactInfoMap should remain the same after sanitization");
    }
}
