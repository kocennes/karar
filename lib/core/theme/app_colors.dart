import 'package:flutter/material.dart';

class AppColors {
  // Vote colors - app identity. Tuned for WCAG AA with white text.
  static const hakli = Color(0xFF15803D);
  static const haksiz = Color(0xFFDC2626);
  static const hakliDim = Color(0xFF14532D);
  static const haksizDim = Color(0xFF7F1D1D);

  // Brand and CTA colors.
  static const primary = Color(0xFF4F46E5);
  static const accent = haksiz;

  // Light mode.
  static const background = Color(0xFFF8F9FA);
  static const surface = Color(0xFFFFFFFF);
  static const surfaceVariant = Color(0xFFF0F2F5);
  static const border = Color(0xFFE1E4E8);
  static const textPrimary = Color(0xFF0D0D0D);
  static const textSecondary = Color(0xFF4B5563);
  static const textTertiary = Color(0xFF6B7280);

  // Dark mode.
  static const darkBackground = Color(0xFF0D0D0D);
  static const darkSurface = Color(0xFF1A1A1A);
  static const darkSurfaceVariant = Color(0xFF222222);
  static const darkBorder = Color(0xFF2A2A2A);
  static const darkTextPrimary = Color(0xFFF1F5F9);
  static const darkTextSecondary = Color(0xFFD1D5DB);
  static const darkTextTertiary = Color(0xFF9CA3AF);

  // Status.
  static const success = hakli;
  static const warning = Color(0xFF92400E);
  static const error = haksiz;
}
