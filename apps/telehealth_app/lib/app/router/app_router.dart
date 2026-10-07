import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

final GoRouter appRouter = GoRouter(
  initialLocation: '/login',
  routes: [
    GoRoute(
      path: '/login',
      builder: (context, state) {
        return const Scaffold(
          body: Center(
            child: Text('Login'),
          ),
        );
      },
    ),

    GoRoute(
      path: '/home',
      builder: (context, state) {
        return const Scaffold(
          body: Center(
            child: Text('Home'),
          ),
        );
      },
    ),
  ],
);