using System;
using System.Reflection;

using Amazon.Runtime;

namespace Cythral.CloudFormation.Resources.Tests
{
    public class TestUtils
    {
        public static void AssertClientHasCredentials(AmazonServiceClient client, AWSCredentials credentials)
        {
            var prop = client.GetType().GetProperty("Credentials", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            var propValue = (AWSCredentials?)prop?.GetValue(client);
            var actualCredentials = (propValue)?.GetCredentials();
            var givenCredentials = credentials.GetCredentials();

            if (actualCredentials?.AccessKey != givenCredentials.AccessKey ||
                actualCredentials?.SecretKey != givenCredentials.SecretKey ||
                actualCredentials?.Token != givenCredentials.Token)
            {
                throw new Exception("Credentials do not match");
            }
        }

        public static void SetPrivateProperty<T, U>(T target, string name, U value)
        {
            var prop = target?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            prop?.SetValue(target, value);
        }

        public static void SetPrivateField<T, U>(T target, string name, U value)
        {
            var field = target?.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            field?.SetValue(target, value);
        }

        public static void SetReadonlyField<T, U>(T target, string name, U value)
        {
            var field = target?.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            field?.SetValue(target, value);
        }

        public static void SetPrivateStaticField<T>(Type target, string name, T value)
        {
            var prop = target?.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            prop?.SetValue(target, value);
        }
    }
}