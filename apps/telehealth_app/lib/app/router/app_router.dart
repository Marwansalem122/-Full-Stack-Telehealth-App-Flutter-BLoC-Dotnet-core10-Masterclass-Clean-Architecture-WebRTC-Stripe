import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:telehealth_app/app/router/route_names.dart';
import 'package:telehealth_app/features/onboarding/presentation/pages/onboarding_page.dart';

final GoRouter appRouter = GoRouter(
  initialLocation: RouteNames.onboarding,
  routes: [
    GoRoute(
      path: RouteNames.onboarding,
      builder: (context, state) {
        return const OnboardingPage();
      },
    ),


    GoRoute(
      path: RouteNames.login,
      builder: (context, state) {
        return const Scaffold(
          body: Center(
            child: Text('Login'),
          ),
        );
      },
    ),
  ],
);