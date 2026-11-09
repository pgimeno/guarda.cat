resource "aws_sesv2_email_identity" "domain" {
  email_identity = var.ses_domain
}

resource "aws_sesv2_email_identity" "demo_recipient" {
  email_identity = var.ses_demo_recipient
}
