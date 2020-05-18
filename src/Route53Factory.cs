using Amazon.Route53;

namespace Cythral.CloudFormation.Resources.Factories
{
    public class Route53Factory
    {
        public virtual IAmazonRoute53 Create()
        {
            return new AmazonRoute53Client();
        }
    }
}