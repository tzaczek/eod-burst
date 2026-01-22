# EOD Burst System - AWS Deployment Script (PowerShell)
# Usage: .\deploy.ps1 -KeyName "your-ssh-key-name"

param(
    [Parameter(Mandatory=$true)]
    [string]$KeyName,
    
    [string]$InstanceType = "t3.xlarge",
    [string]$Region = "us-east-1"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  EOD Burst System - AWS Deployment" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# Set region
$env:AWS_DEFAULT_REGION = $Region

# Check AWS CLI
Write-Host "Checking AWS CLI..." -ForegroundColor Yellow
try {
    aws sts get-caller-identity | Out-Null
    Write-Host "  AWS CLI configured ✓" -ForegroundColor Green
} catch {
    Write-Host "  ERROR: AWS CLI not configured. Run 'aws configure' first." -ForegroundColor Red
    exit 1
}

# Create security group
Write-Host ""
Write-Host "Creating security group..." -ForegroundColor Yellow
try {
    $sgId = aws ec2 create-security-group `
        --group-name "eod-burst-sg" `
        --description "EOD Burst System" `
        --query 'GroupId' `
        --output text 2>$null
    Write-Host "  Created security group: $sgId" -ForegroundColor Green
} catch {
    $sgId = aws ec2 describe-security-groups `
        --group-names "eod-burst-sg" `
        --query 'SecurityGroups[0].GroupId' `
        --output text
    Write-Host "  Using existing security group: $sgId" -ForegroundColor Yellow
}

# Open ports
Write-Host ""
Write-Host "Opening ports..." -ForegroundColor Yellow
$ports = @(22, 8080, 8081, 8082, 8083, 3000, 9090, 16686, 8090, 8091, 9000, 9001)
foreach ($port in $ports) {
    try {
        aws ec2 authorize-security-group-ingress `
            --group-id $sgId `
            --protocol tcp `
            --port $port `
            --cidr "0.0.0.0/0" 2>$null
    } catch {}
}
Write-Host "  Ports opened ✓" -ForegroundColor Green

# Get latest Amazon Linux 2023 AMI
Write-Host ""
Write-Host "Finding latest Amazon Linux 2023 AMI..." -ForegroundColor Yellow
$amiId = aws ec2 describe-images `
    --owners amazon `
    --filters "Name=name,Values=al2023-ami-2023*-x86_64" "Name=state,Values=available" `
    --query 'sort_by(Images, &CreationDate)[-1].ImageId' `
    --output text
Write-Host "  AMI: $amiId" -ForegroundColor Green

# Read user-data script
$scriptPath = Join-Path $PSScriptRoot "setup-ec2.sh"
$userData = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes((Get-Content $scriptPath -Raw)))

# Launch instance
Write-Host ""
Write-Host "Launching EC2 instance ($InstanceType)..." -ForegroundColor Yellow
$instanceId = aws ec2 run-instances `
    --image-id $amiId `
    --instance-type $InstanceType `
    --key-name $KeyName `
    --security-group-ids $sgId `
    --user-data $userData `
    --tag-specifications "ResourceType=instance,Tags=[{Key=Name,Value=eod-burst-system}]" `
    --block-device-mappings "[{`"DeviceName`":`"/dev/xvda`",`"Ebs`":{`"VolumeSize`":50,`"VolumeType`":`"gp3`"}}]" `
    --query 'Instances[0].InstanceId' `
    --output text

Write-Host "  Instance ID: $instanceId" -ForegroundColor Green

# Wait for instance to be running
Write-Host ""
Write-Host "Waiting for instance to start..." -ForegroundColor Yellow
aws ec2 wait instance-running --instance-ids $instanceId
Write-Host "  Instance running ✓" -ForegroundColor Green

# Get public IP
$publicIp = aws ec2 describe-instances `
    --instance-ids $instanceId `
    --query 'Reservations[0].Instances[0].PublicIpAddress' `
    --output text

Write-Host ""
Write-Host "==========================================" -ForegroundColor Green
Write-Host "  Deployment Complete!" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Instance ID: $instanceId" -ForegroundColor White
Write-Host "  Public IP:   $publicIp" -ForegroundColor White
Write-Host ""
Write-Host "  Wait ~5 minutes for services to start, then access:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  🧪 Test Dashboard: http://$publicIp`:8083" -ForegroundColor Cyan
Write-Host "  📊 Grafana:        http://$publicIp`:3000" -ForegroundColor Cyan
Write-Host "  🔍 Jaeger:         http://$publicIp`:16686" -ForegroundColor Cyan
Write-Host "  📨 Kafka UI:       http://$publicIp`:8090" -ForegroundColor Cyan
Write-Host "  📦 MinIO:          http://$publicIp`:9001" -ForegroundColor Cyan
Write-Host ""
Write-Host "  SSH: ssh -i $KeyName.pem ec2-user@$publicIp" -ForegroundColor Gray
Write-Host ""
Write-Host "  To stop (save money): aws ec2 stop-instances --instance-ids $instanceId" -ForegroundColor Gray
Write-Host "  To terminate:         aws ec2 terminate-instances --instance-ids $instanceId" -ForegroundColor Gray
Write-Host ""
